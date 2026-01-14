using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Contracts.Validation;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.SK.Conversation;
using TILSOFTAI.Orchestration.SK.Governance;
using TILSOFTAI.Orchestration.SK.Planning;
using TILSOFTAI.Orchestration.SK.Plugins;
using TILSOFTAI.Orchestration.Tools;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ChatPipeline
{
    private readonly SkKernelFactory _kernelFactory;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly CommitGuardFilter _commitGuard;
    private readonly AutoInvocationCircuitBreakerFilter _circuitBreaker;
    private readonly TokenBudget _tokenBudget;
    private readonly IAuditLogger _auditLogger;
    private readonly PlannerRouter _plannerRouter;
    private readonly StepwiseLoopRunner _stepwiseLoopRunner;
    private readonly IConversationStateStore _conversationState;
    private readonly ILanguageResolver _languageResolver;
    private readonly IChatTextLocalizer _localizer;
    private readonly ChatTextPatterns _patterns;
    private readonly ChatTuningOptions _tuning;
    private readonly ILogger<ChatPipeline> _logger;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _serviceProvider;
    private readonly PluginCatalog _pluginCatalog;
    private readonly ModuleRouter _moduleRouter;
    private readonly IEnumerable<IPluginExposurePolicy> _exposurePolicies;

    //Plugin

    public ChatPipeline(
        SkKernelFactory kernelFactory,
        ExecutionContextAccessor ctxAccessor,
        ToolRegistry toolRegistry,
        ToolDispatcher toolDispatcher,
        CommitGuardFilter commitGuard,
        AutoInvocationCircuitBreakerFilter circuitBreaker,
        TokenBudget tokenBudget,
        IAuditLogger auditLogger,
        PlannerRouter plannerRouter,
        StepwiseLoopRunner stepwiseLoopRunner,
        IConversationStateStore conversationState,
        IServiceProvider serviceProvider,
        PluginCatalog pluginCatalog,
        ModuleRouter moduleRouter,
        IEnumerable<IPluginExposurePolicy> exposurePolicies,
        ILanguageResolver languageResolver,
        IChatTextLocalizer localizer,
        ChatTextPatterns patterns,
        ChatTuningOptions tuning,
        ILogger<ChatPipeline>? logger = null)
    {
        _kernelFactory = kernelFactory;
        _ctxAccessor = ctxAccessor;
        _toolRegistry = toolRegistry;
        _toolDispatcher = toolDispatcher;
        _commitGuard = commitGuard;
        _circuitBreaker = circuitBreaker;
        _tokenBudget = tokenBudget;
        _auditLogger = auditLogger;
        _plannerRouter = plannerRouter;
        _stepwiseLoopRunner = stepwiseLoopRunner;
        _conversationState = conversationState;
        _languageResolver = languageResolver;
        _localizer = localizer;
        _patterns = patterns;
        _tuning = tuning;
        _serviceProvider = serviceProvider;
        _pluginCatalog = pluginCatalog;
        _moduleRouter = moduleRouter;
        _exposurePolicies = exposurePolicies;
        _logger = logger ?? NullLogger<ChatPipeline>.Instance;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);

        var incomingMessages = request.Messages ?? Array.Empty<ChatCompletionMessage>();

        if (incomingMessages.Any(m => !IsSupportedRole(m.Role)))
        {
            return BuildFailureResponse("Unsupported message role.", request.Model, Array.Empty<ChatCompletionMessage>());
        }

        var hasUser = incomingMessages.Any(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.Content));

        if (!hasUser)
        {
            return BuildFailureResponse("User message required.", request.Model, Array.Empty<ChatCompletionMessage>());
        }

        var lastUserMessage = incomingMessages.Last(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.Content)).Content;

        // Build a routing text that is robust for short follow-up questions like
        // "mùa 24/25?" which rely on prior context (e.g., the previous user question).
        var routingText = BuildRoutingText(incomingMessages, lastUserMessage);

        // Conversation state helps route short follow-ups that omit the subject (e.g., "mùa 23/24?").
        // It also allows server-side filter patching in ToolInvoker.
        var conversationState = await _conversationState.TryGetAsync(context, cancellationToken);

        // Allow user to reset filter context explicitly.
        if (_patterns.IsResetFiltersIntent(lastUserMessage))
        {
            await _conversationState.ClearAsync(context, cancellationToken);
            conversationState = null;
        }

        // Resolve response language for this turn (VI/EN) and persist it per conversation.
        var lang = _languageResolver.Resolve(incomingMessages, conversationState);
        conversationState ??= new ConversationState();
        if (!string.Equals(conversationState.PreferredLanguage, lang.ToIsoCode(), StringComparison.OrdinalIgnoreCase))
        {
            conversationState.PreferredLanguage = lang.ToIsoCode();
            await _conversationState.UpsertAsync(context, conversationState, cancellationToken);
        }

        // 1) set context for plugins/filters
        _ctxAccessor.CircuitBreakerTripped = false;
        _ctxAccessor.CircuitBreakerReason = null;
        _ctxAccessor.Context = context;
        _ctxAccessor.ConfirmedConfirmationId = _patterns.TryExtractConfirmationId(lastUserMessage);
        _ctxAccessor.AutoInvokeCount = 0;
        _ctxAccessor.AutoInvokeSignatureCounts.Clear();
        _ctxAccessor.LastTotalCount = null;
        _ctxAccessor.LastStoredProcedure = null;
        _ctxAccessor.LastSeasonFilter = null;
        _ctxAccessor.LastCollectionFilter = null;
        _ctxAccessor.LastRangeNameFilter = null;
        _ctxAccessor.LastDisplayPreviewJson = null;

        _logger.LogInformation("ChatPipeline start requestId={RequestId} traceId={TraceId} convId={ConversationId} userId={UserId} lang={Lang} model={Model} msgs={MsgCount}",
            context.RequestId, context.TraceId, context.ConversationId, context.UserId, lang.ToIsoCode(), request.Model, incomingMessages.Count);

        // 2) build kernel
        var kernel = _kernelFactory.CreateKernel(request.Model);

        // 3) register module plugins (module selection must consider short follow-ups)
        var modules = _moduleRouter.SelectModules(routingText, context);

        // If router cannot detect a module for a short follow-up, fall back to the previous module.
        if (modules.Count == 0 && conversationState?.LastQuery is not null)
        {
            var module = conversationState.LastQuery.Resource.Split('.', 2, StringSplitOptions.RemoveEmptyEntries)[0];
            if (!string.IsNullOrWhiteSpace(module))
            {
                modules = new[] { module, "common" };
            }
        }

        foreach (var module in modules)
        {
            if (!_pluginCatalog.ByModule.TryGetValue(module, out var types)) continue;

            var policy = _exposurePolicies.First(p => p.CanHandle(module));
            var selectedTypes = policy.Select(module, types, routingText);
            if (selectedTypes.Count == 0) selectedTypes = types;

            foreach (var t in selectedTypes)
            {
                var plugin = _serviceProvider.GetRequiredService(t);
                var pluginName = ToPluginName(t.Name);
                kernel.Plugins.AddFromObject(plugin, pluginName);
            }
        }

        // If this looks like an ERP/business question (modules selected) but no tools are
        // actually exposed, do not let the model hallucinate tool calls. Return a clear
        // feature-not-available response instead.
        var exposedFunctionCount = kernel.Plugins.SelectMany(p => p).Count();
        if (modules.Count > 0 && exposedFunctionCount == 0)
        {
            var fallback = _localizer.Get(ChatTextKeys.FeatureNotAvailable, lang);
            return BuildResponse(request.Model ?? "TILSOFT-AI", fallback, incomingMessages);
        }

        // 4) governance filter: chọn 1 trong 2 dòng dưới tùy interface bạn implement
        // Nếu CommitGuardFilter : IAutoFunctionInvocationFilter
        kernel.AutoFunctionInvocationFilters.Add(_circuitBreaker);
        kernel.AutoFunctionInvocationFilters.Add(_commitGuard);
        // Nếu CommitGuardFilter : IFunctionInvocationFilter thì dùng:
        // kernel.FunctionInvocationFilters.Add(_commitGuard);

        // 5) build chat history
        var history = new ChatHistory();

        history.AddSystemMessage(_localizer.Get(ChatTextKeys.SystemPrompt, lang));

        // Provide a compact, machine-readable hint about the last query when the user turn is likely a follow-up.
        // This improves tool selection and filter continuity without hard-coding filter keys.
        if (conversationState?.LastQuery is not null && _patterns.IsLikelyFollowUp(lastUserMessage))
        {
            var hint = JsonSerializer.Serialize(new
            {
                kind = "tilsoft.conversation_state.v1",
                lastQuery = new
                {
                    resource = conversationState.LastQuery.Resource,
                    filters = conversationState.LastQuery.Filters,
                    updatedAtUtc = conversationState.LastQuery.UpdatedAtUtc
                }
            }, _serializerOptions);

            history.AddSystemMessage(_localizer.Get(ChatTextKeys.PreviousQueryHint, lang) + hint);
        }

        foreach (var m in incomingMessages)
        {
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;

            history.AddMessage(ToAuthorRole(m.Role), m.Content);
        }

        // If no tools are exposed, do NOT use tool-calling settings. Some models
        // (LM Studio/open-source) may emit pseudo tool-call text in content when
        // tools are unavailable. We force a plain chat completion instead.
        if (exposedFunctionCount == 0)
        {
            var noToolsContent = await RespondWithoutToolsAsync(request.Model, history, lang, cancellationToken);
            await _auditLogger.LogUserInputAsync(context, serializedRequest, cancellationToken);
            var noToolsResponse = BuildResponse(request.Model ?? "TILSOFT-AI", noToolsContent, incomingMessages);
            _tokenBudget.EnsureWithinBudget(noToolsResponse.Choices.First().Message.Content);
            return noToolsResponse;
        }

        // 6) 01 model - internal routing
        var useLoop = _plannerRouter.ShouldUseLoop(lastUserMessage);

        string content;
        try
        {
            if (useLoop)
            {
                content = await _stepwiseLoopRunner.RunAsync(kernel, history, maxIterations: 8, cancellationToken);
            }
            else
            {
                content = await GetAssistantAnswerAsync(request.Model, kernel, history, lang, cancellationToken);
            }

            // Defensive: if some path returns empty content, force a no-tools synthesis pass.
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("ChatPipeline got empty content after tool/loop path; forcing no-tools synthesis. autoInvokes={AutoInvokes}", _ctxAccessor.AutoInvokeCount);
                content = await SynthesizeWithoutToolsAsync(request.Model, history, lang, cancellationToken);
            }

            // thấy marker / flag thì xóa content để ép synthesis
            _logger.LogWarning("ChatPipeline circuit breaker check tripped={Tripped} reason={Reason} autoInvokes={AutoInvokes} signatures={SigCount} contentPreview={Preview}",
                    _ctxAccessor.CircuitBreakerTripped, _ctxAccessor.CircuitBreakerReason, _ctxAccessor.AutoInvokeCount, _ctxAccessor.AutoInvokeSignatureCounts.Count,
                    content.Length > 200 ? content.Substring(0, 200) : content);

            if (_ctxAccessor.CircuitBreakerTripped ||
                content.StartsWith("CIRCUIT_BREAKER", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("\"circuit_breaker\"", StringComparison.OrdinalIgnoreCase))
            {
                // Circuit breaker triggered: do NOT call the LLM again. Build a deterministic fallback
                // using the last known tool evidence captured during this request.
                content = BuildCircuitBreakerFallback(lang);
            }

            // Absolute safety: never return empty assistant content to the client.
            if (string.IsNullOrWhiteSpace(content))
            {
                content = _localizer.Get(ChatTextKeys.FallbackNoContent, lang);
            }
        }
        catch (ToolContractViolationException ex)
        {
            // Non-retryable server-side contract drift. Do not call the LLM again.
            return BuildFailureResponse(ex.Message, request.Model, incomingMessages);
        }
        catch
        {
            return BuildFailureResponse("AI response unavailable.", request.Model, incomingMessages);
        }

        await _auditLogger.LogUserInputAsync(context, serializedRequest, cancellationToken);

        _logger.LogInformation("ChatPipeline end breakerTripped={Tripped} autoInvokes={AutoInvokes} finalContentLen={Len}", _ctxAccessor.CircuitBreakerTripped, _ctxAccessor.AutoInvokeCount, content?.Length ?? 0);

        var response = BuildResponse(request.Model ?? "TILSOFT-AI", content, incomingMessages);
        _tokenBudget.EnsureWithinBudget(response.Choices.First().Message.Content);
        return response;
    }

    private async Task<string> GetAssistantAnswerAsync(
        string? requestedModel,
        Kernel kernel,
        ChatHistory history,
        ChatLanguage lang,
        CancellationToken cancellationToken)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = (float)_tuning.ToolCallTemperature
        };

        var msg = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
        var content = (msg.Content ?? string.Empty).Trim();

        // If the model executed tools but returned empty content, do a forced synthesis
        // pass with no tools exposed. This prevents empty assistant outputs from reaching
        // the client and avoids relying on any sentinel tokens.
        if (string.IsNullOrWhiteSpace(content))
        {
            content = await SynthesizeWithoutToolsAsync(requestedModel, history, lang, cancellationToken);
        }

        return content;
    }

    private async Task<string> RespondWithoutToolsAsync(
        string? requestedModel,
        ChatHistory history,
        ChatLanguage lang,
        CancellationToken cancellationToken)
    {
        // New kernel without plugins => no tools can be called.
        // This avoids LM Studio/open-source models emitting pseudo tool-call text.
        var kernel2 = _kernelFactory.CreateKernel(requestedModel);
        var chat2 = kernel2.GetRequiredService<IChatCompletionService>();

        // Add a short instruction: answer normally, do not attempt tool calling.
        history.AddSystemMessage(_localizer.Get(ChatTextKeys.NoToolsMode, lang));

        var msg2 = await chat2.GetChatMessageContentAsync(history, new OpenAIPromptExecutionSettings { Temperature = (float)_tuning.NoToolsTemperature }, kernel2, cancellationToken);
        var final = (msg2.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(final))
            final = _localizer.Get(ChatTextKeys.FallbackNoContent, lang);
        return final;
    }

    private string BuildCircuitBreakerFallback(ChatLanguage lang)
    {
        // Prefer deterministic metrics captured from the last successful atomic.query.execute.
        if (_ctxAccessor.LastTotalCount is not null)
        {
            var sp = _ctxAccessor.LastStoredProcedure ?? "(unknown)";
            var season = _ctxAccessor.LastSeasonFilter;
            var collection = _ctxAccessor.LastCollectionFilter;
            var range = _ctxAccessor.LastRangeNameFilter;
            var preview = _ctxAccessor.LastDisplayPreviewJson;

            if (lang == ChatLanguage.En)
            {
                var filterText = BuildFilterTextEn(season, collection, range);
                var baseText =
                    $"Total models: {_ctxAccessor.LastTotalCount}." +
                    (string.IsNullOrWhiteSpace(filterText) ? string.Empty : $" Filters: {filterText}.") +
                    $" (source: {sp})";

                if (!string.IsNullOrWhiteSpace(preview))
                    baseText += "\n\nPreview:\n" + preview;

                return baseText;
            }
            else
            {
                var filterText = BuildFilterTextVi(season, collection, range);
                var baseText =
                    $"Tổng số model: {_ctxAccessor.LastTotalCount}." +
                    (string.IsNullOrWhiteSpace(filterText) ? string.Empty : $" Bộ lọc: {filterText}.") +
                    $" (nguồn: {sp})";

                if (!string.IsNullOrWhiteSpace(preview))
                    baseText += "\n\nPreview:\n" + preview;

                return baseText;
            }
        }

        // As a safe last resort, return a non-empty localized message.
        return _localizer.Get(ChatTextKeys.FallbackNoContent, lang);
    }

    private static string BuildFilterTextEn(string? season, string? collection, string? range)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(season)) parts.Add($"season={season}");
        if (!string.IsNullOrWhiteSpace(collection)) parts.Add($"collection={collection}");
        if (!string.IsNullOrWhiteSpace(range)) parts.Add($"range={range}");
        return string.Join(", ", parts);
    }

    private static string BuildFilterTextVi(string? season, string? collection, string? range)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(season)) parts.Add($"mùa={season}");
        if (!string.IsNullOrWhiteSpace(collection)) parts.Add($"bộ sưu tập={collection}");
        if (!string.IsNullOrWhiteSpace(range)) parts.Add($"range={range}");
        return string.Join(", ", parts);
    }

    private async Task<string> SynthesizeWithoutToolsAsync(
        string? requestedModel,
        ChatHistory history,
        ChatLanguage lang,
        CancellationToken cancellationToken)
    {
        // Add a short instruction to produce a final answer based on evidence already in history.
        history.AddSystemMessage(_localizer.Get(ChatTextKeys.SynthesizeNoTools, lang));

        // New kernel without plugins => no tools can be called.
        var kernel2 = _kernelFactory.CreateKernel(requestedModel);
        var chat2 = kernel2.GetRequiredService<IChatCompletionService>();
        var msg2 = await chat2.GetChatMessageContentAsync(history, new OpenAIPromptExecutionSettings { Temperature = (float)_tuning.SynthesisTemperature }, kernel2, cancellationToken);
        var final = (msg2.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(final))
            final = _localizer.Get(ChatTextKeys.FallbackNoContent, lang);
        return final;
    }

    private ChatCompletionResponse BuildFailureResponse(string error, string? model, IReadOnlyCollection<ChatCompletionMessage> promptMessages)
    {
        var payload = JsonSerializer.Serialize(new { error }, _serializerOptions);
        return BuildResponse(model, payload, promptMessages);
    }

    private ChatCompletionResponse BuildResponse(string? model, string content, IReadOnlyCollection<ChatCompletionMessage> promptMessages)
    {
        var completionTokens = _tokenBudget.EstimateTokens(content);
        var promptTokens = _tokenBudget.EstimateMessageTokens(promptMessages);

        return new ChatCompletionResponse
        {
            Id = $"chatcmpl-{Guid.NewGuid():N}",
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model ?? "lmstudio",
            Choices = new[]
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new ChatCompletionMessage
                    {
                        Role = "assistant",
                        Content = content
                    }
                }
            },
            Usage = new ChatUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens
            }
        };
    }

    //private static bool IsSupportedRole(string? role)
    //{
    //    return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
    //           string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ||
    //           string.Equals(role, "system", StringComparison.OrdinalIgnoreCase);
    //}
    private static AuthorRole ToAuthorRole(string? role)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)) return AuthorRole.User;
        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return AuthorRole.Assistant;
        if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase)) return AuthorRole.System;

        // fallback an toàn: coi như user
        return AuthorRole.User;
    }

    private static bool IsSupportedRole(string? role)
    {
        return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "system", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildRoutingText(IReadOnlyCollection<ChatCompletionMessage> messages, string lastUserMessage)
    {
        var last = (lastUserMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(last)) return string.Empty;

        var parts = new List<string>();

        if (_patterns.IsLikelyFollowUp(last))
        {
            var prevUsers = messages.Reverse()
                .Skip(1)
                .Where(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content))
                .Take(3)
                .Select(m => m.Content!)
                .Reverse();

            foreach (var u in prevUsers)
                parts.Add(u);

            var prevAssistant = messages.Reverse()
                .FirstOrDefault(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content))
                ?.Content;

            if (!string.IsNullOrWhiteSpace(prevAssistant))
                parts.Add(prevAssistant!);
        }

        parts.Add(last);
        var routing = string.Join(" ", parts);

        var max = _tuning.MaxRoutingContextChars <= 0 ? 1200 : _tuning.MaxRoutingContextChars;
        if (routing.Length > max)
            routing = routing[^max..];

        return routing;
    }

    private static string ToPluginName(string typeName)
    {
        const string suffix = "ToolsPlugin";
        var name = typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName[..^suffix.Length]
            : typeName;

        if (string.IsNullOrWhiteSpace(name)) return "common";
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

}
