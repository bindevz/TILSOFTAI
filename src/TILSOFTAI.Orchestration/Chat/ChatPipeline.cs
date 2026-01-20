using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Llm.OpenAi;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.SK.Conversation;

namespace TILSOFTAI.Orchestration.Chat;

/// <summary>
/// Manual tool-calling loop (Mode B):
/// - Call LLM with OpenAI "tools" schema
/// - Execute tool calls via ToolInvoker
/// - Append tool results back to the conversation
/// - Final synthesis pass with tools disabled
/// </summary>
public sealed class ChatPipeline
{
    private static readonly HashSet<string> SupportedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "user", "assistant", "tool"
    };

    private readonly OpenAiChatClient _chat;
    private readonly OpenAiToolSchemaFactory _toolSchemaFactory;
    private readonly ToolInvoker _toolInvoker;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly TokenBudget _tokenBudget;
    private readonly IAuditLogger _auditLogger;
    private readonly IConversationStateStore _conversationState;
    private readonly ILanguageResolver _languageResolver;
    private readonly IChatTextLocalizer _localizer;
    private readonly ChatTextPatterns _patterns;
    private readonly ChatTuningOptions _tuning;
    private readonly ILogger<ChatPipeline> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // Tool list exposed to LLM for this Orchestrator.
    private static readonly string[] ExposedTools =
    [
        "atomic.catalog.search",
        "atomic.query.execute",
        "analytics.run"
    ];

    public ChatPipeline(
        OpenAiChatClient chat,
        OpenAiToolSchemaFactory toolSchemaFactory,
        ToolInvoker toolInvoker,
        ExecutionContextAccessor ctxAccessor,
        TokenBudget tokenBudget,
        IAuditLogger auditLogger,
        IConversationStateStore conversationState,
        ILanguageResolver languageResolver,
        IChatTextLocalizer localizer,
        ChatTextPatterns patterns,
        ChatTuningOptions tuning,
        ILogger<ChatPipeline>? logger = null)
    {
        _chat = chat;
        _toolSchemaFactory = toolSchemaFactory;
        _toolInvoker = toolInvoker;
        _ctxAccessor = ctxAccessor;
        _tokenBudget = tokenBudget;
        _auditLogger = auditLogger;
        _conversationState = conversationState;
        _languageResolver = languageResolver;
        _localizer = localizer;
        _patterns = patterns;
        _tuning = tuning;
        _logger = logger ?? NullLogger<ChatPipeline>.Instance;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, TILSOFTAI.Domain.ValueObjects.TSExecutionContext context, CancellationToken cancellationToken)
    {
        var incomingMessages = request.Messages ?? Array.Empty<ChatCompletionMessage>();

        if (incomingMessages.Any(m => !SupportedRoles.Contains(m.Role)))
            return BuildFailureResponse("Unsupported message role.", request.Model, incomingMessages);

        var hasUser = incomingMessages.Any(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content));
        if (!hasUser)
            return BuildFailureResponse("User message required.", request.Model, incomingMessages);

        var lastUserMessage = incomingMessages.Last(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content)).Content;

        // Audit: user input (best-effort)
        try
        {
            await _auditLogger.LogUserInputAsync(context, lastUserMessage, cancellationToken);
        }
        catch { /* ignore */ }


        // Conversation state + reset filters
        var conversationState = await _conversationState.TryGetAsync(context, cancellationToken);
        if (_patterns.IsResetFiltersIntent(lastUserMessage))
        {
            await _conversationState.ClearAsync(context, cancellationToken);
            conversationState = null;
        }

        // Resolve response language and persist.
        var lang = _languageResolver.Resolve(incomingMessages, conversationState);
        conversationState ??= new ConversationState();
        if (!string.Equals(conversationState.PreferredLanguage, lang.ToIsoCode(), StringComparison.OrdinalIgnoreCase))
        {
            conversationState.PreferredLanguage = lang.ToIsoCode();
            await _conversationState.UpsertAsync(context, conversationState, cancellationToken);
        }

        // Set request context for ToolInvoker
        _ctxAccessor.Context = context;
        _ctxAccessor.ConfirmedConfirmationId = _patterns.TryExtractConfirmationId(lastUserMessage);
        _ctxAccessor.AutoInvokeCount = 0;
        _ctxAccessor.CircuitBreakerTripped = false;
        _ctxAccessor.CircuitBreakerReason = null;
        _ctxAccessor.AutoInvokeSignatureCounts.Clear();
        _ctxAccessor.LastTotalCount = null;
        _ctxAccessor.LastStoredProcedure = null;
        _ctxAccessor.LastFilters = null;
        _ctxAccessor.LastSeasonFilter = null;
        _ctxAccessor.LastCollectionFilter = null;
        _ctxAccessor.LastRangeNameFilter = null;
        _ctxAccessor.LastDisplayPreviewJson = null;

        _logger.LogInformation("ChatPipeline start requestId={RequestId} traceId={TraceId} convId={ConversationId} userId={UserId} lang={Lang} model={Model} msgs={MsgCount}",
            context.RequestId, context.TraceId, context.ConversationId, context.UserId, lang.ToIsoCode(), request.Model, incomingMessages.Count);

        // Build OpenAI messages
        var systemPrompt = BuildSystemPrompt(lang, conversationState);
        var messages = new List<OpenAiChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        foreach (var m in incomingMessages)
        {
            // Do not allow client to override system prompt.
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                continue;

            messages.Add(new OpenAiChatMessage { Role = m.Role, Content = m.Content });
        }

        // Build tools
        var tools = _toolSchemaFactory.BuildTools(ExposedTools);

        // Planner loop
        var mappedModel = _chat.MapModel(request.Model);
        var maxSteps = Math.Clamp(_tuning.MaxToolSteps, 1, 20);

        OpenAiChatCompletionResponse? lastResp = null;

        for (var step = 0; step < maxSteps; step++)
        {
            var call = new OpenAiChatCompletionRequest
            {
                Model = mappedModel,
                Messages = messages,
                Tools = tools.ToList(),
                ToolChoice = "auto",
                Temperature = request.Temperature ?? _tuning.ToolCallTemperature,
                MaxTokens = request.MaxTokens ?? _tuning.MaxTokens,
                Stream = false
            };

            lastResp = await _chat.CreateChatCompletionAsync(call, cancellationToken);
            var choice = lastResp.Choices.FirstOrDefault();
            if (choice?.Message is null)
                break;

            var assistantMsg = choice.Message;
            messages.Add(assistantMsg);

            // No tool calls -> final answer candidate
            if (assistantMsg.ToolCalls is null || assistantMsg.ToolCalls.Count == 0)
            {
                var final = assistantMsg.Content;
                if (string.IsNullOrWhiteSpace(final))
                    final = _localizer.Get(ChatTextKeys.FallbackNoContent, lang);

                // Ensure final output is Markdown 3-block. Run synthesis pass with tools disabled.
                final = await SynthesizeFinalAsync(mappedModel, messages, lang, cancellationToken);

                // Audit: AI output (best-effort)
                try
                {
                    await _auditLogger.LogAiDecisionAsync(context, final, cancellationToken);
                }
                catch { /* ignore */ }

                return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", final, incomingMessages);
            }

            // Execute tools
            foreach (var tc in assistantMsg.ToolCalls)
            {
                var toolName = tc.Function.Name;
                var argsJson = tc.Function.Arguments ?? "{}";

                // Circuit breaker by signature
                var signature = ComputeSignature(toolName, argsJson);
                _ctxAccessor.AutoInvokeSignatureCounts.TryGetValue(signature, out var count);
                count++;
                _ctxAccessor.AutoInvokeSignatureCounts[signature] = count;
                if (count > 2)
                {
                    _ctxAccessor.CircuitBreakerTripped = true;
                    _ctxAccessor.CircuitBreakerReason = $"Repeated tool call signature: {toolName}";

                    var forced = await SynthesizeFinalAsync(mappedModel, messages, lang, cancellationToken);
                    return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", forced, incomingMessages);
                }

                JsonElement argsElement;
                try
                {
                    using var doc = JsonDocument.Parse(argsJson);
                    argsElement = doc.RootElement.Clone();
                }
                catch
                {
                    using var doc = JsonDocument.Parse("{}");
                    argsElement = doc.RootElement.Clone();
                }

                var toolResult = await _toolInvoker.ExecuteAsync(toolName, argsElement, cancellationToken);
                var toolResultJson = JsonSerializer.Serialize(toolResult, _json);

                messages.Add(new OpenAiChatMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Content = toolResultJson
                });
            }
        }

        // Step limit reached -> synthesize anyway.
        var fallbackFinal = await SynthesizeFinalAsync(mappedModel, messages, lang, cancellationToken);
        return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", fallbackFinal, incomingMessages);
    }

    private string BuildSystemPrompt(ChatLanguage lang, ConversationState? state)
    {
        var basePrompt = _localizer.Get(ChatTextKeys.SystemPrompt, lang);

        // Provide prior query hint to help short follow-ups.
        if (state?.LastQuery is not null)
        {
            var hint = _localizer.Get(ChatTextKeys.PreviousQueryHint, lang);
            basePrompt += "\n\n" + hint + JsonSerializer.Serialize(state.LastQuery, _json);
        }

        basePrompt += "\n\nOUTPUT FORMAT (Markdown only):\n" +
                      "- ## Kết luận / Insight\n" +
                      "- ## Preview dữ liệu của Kết luận / Insight (Markdown table)\n" +
                      "- ## Preview danh sách (Markdown table, chỉ khi có dữ liệu danh sách)\n";

        basePrompt += "\n\nTool rule: nếu cần chọn stored procedure theo domain/ngữ cảnh, luôn gọi atomic.catalog.search trước, sau đó atomic.query.execute. Nếu cần phân tích sâu (group/pivot/sum/avg/topN), dùng analytics.run với datasetId từ atomic.query.execute.";

        return basePrompt;
    }

    private async Task<string> SynthesizeFinalAsync(string mappedModel, List<OpenAiChatMessage> messages, ChatLanguage lang, CancellationToken ct)
    {
        // Build a synthesis request: tools disabled.
        var instruction = _localizer.Get(ChatTextKeys.SynthesizeNoTools, lang) +
                         "\n\nBắt buộc output theo đúng 3 mục Markdown như sau:\n" +
                         "## Kết luận / Insight\n...\n\n## Preview dữ liệu của Kết luận / Insight\n(bảng markdown)\n\n## Preview danh sách\n(nếu có)";

        // Ensure the synthesis instruction is applied as system prompt for the final call.
        var synthMessages = new List<OpenAiChatMessage>();
        var firstSystem = messages.FirstOrDefault(m => m.Role == "system");
        var system = (firstSystem?.Content ?? string.Empty) + "\n\n" + instruction;
        synthMessages.Add(new OpenAiChatMessage { Role = "system", Content = system });

        foreach (var m in messages.Skip(1))
            synthMessages.Add(m);

        var req = new OpenAiChatCompletionRequest
        {
            Model = mappedModel,
            Messages = synthMessages,
            Tools = null,
            ToolChoice = null,
            Temperature = _tuning.SynthesisTemperature,
            MaxTokens = Math.Clamp(_tuning.MaxTokens, 256, 4000),
            Stream = false
        };

        var resp = await _chat.CreateChatCompletionAsync(req, ct);
        var msg = resp.Choices.FirstOrDefault()?.Message;
        var content = msg?.Content;

        if (string.IsNullOrWhiteSpace(content))
            content = _localizer.Get(ChatTextKeys.FallbackNoContent, lang);

        return content;
    }

    private static string ComputeSignature(string toolName, string argsJson)
    {
        var input = Encoding.UTF8.GetBytes(toolName + "|" + argsJson);
        var hash = SHA256.HashData(input);
        return toolName + ":" + Convert.ToHexString(hash);
    }

    private ChatCompletionResponse MapToApiResponse(OpenAiChatCompletionResponse? raw, string model, string assistantContent, IReadOnlyCollection<ChatCompletionMessage> incoming)
    {
        var usage = raw?.Usage;
        var apiUsage = new ChatUsage
        {
            PromptTokens = usage?.PromptTokens ?? 0,
            CompletionTokens = usage?.CompletionTokens ?? 0,
            TotalTokens = usage?.TotalTokens ?? 0
        };

        return new ChatCompletionResponse
        {
            Id = raw?.Id ?? Guid.NewGuid().ToString("N"),
            Object = raw?.Object ?? "chat.completion",
            Created = raw?.Created ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model,
            Usage = apiUsage,
            Choices = new[]
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new ChatCompletionMessage { Role = "assistant", Content = assistantContent }
                }
            }
        };
    }

    private static ChatCompletionResponse BuildFailureResponse(string message, string? model, IReadOnlyCollection<ChatCompletionMessage> incoming)
    {
        return new ChatCompletionResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model ?? "TILSOFT-AI",
            Usage = new ChatUsage { PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0 },
            Choices = new[]
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new ChatCompletionMessage { Role = "assistant", Content = message }
                }
            }
        };
    }
}
