using System.Text.Json;
using System.Security;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ChatPipeline
{
    private readonly LmStudioClient _lmStudioClient;
    private readonly ResponseParser _responseParser;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly ContextManager _contextManager;
    private readonly TokenBudget _tokenBudget;
    private readonly IAuditLogger _auditLogger;
    private readonly RbacService _rbacService;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public ChatPipeline(
        LmStudioClient lmStudioClient,
        ResponseParser responseParser,
        ToolRegistry toolRegistry,
        ToolDispatcher toolDispatcher,
        ContextManager contextManager,
        TokenBudget tokenBudget,
        IAuditLogger auditLogger,
        RbacService rbacService)
    {
        _lmStudioClient = lmStudioClient;
        _responseParser = responseParser;
        _toolRegistry = toolRegistry;
        _toolDispatcher = toolDispatcher;
        _contextManager = contextManager;
        _tokenBudget = tokenBudget;
        _auditLogger = auditLogger;
        _rbacService = rbacService;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        await _auditLogger.LogUserInputAsync(context, JsonSerializer.Serialize(request, _serializerOptions), cancellationToken);

        var incomingMessages = request.Messages ?? Array.Empty<ChatCompletionMessage>();
        var trimmedMessages = _contextManager.PrepareMessages(incomingMessages);
        var systemPrompt = PromptPolicy.BuildPrompt(_toolRegistry.GetToolNames());
        var rawContent = await _lmStudioClient.GetToolIntentAsync(request.Model, trimmedMessages, systemPrompt, cancellationToken);
        await _auditLogger.LogAiDecisionAsync(context, rawContent ?? string.Empty, cancellationToken);

        var invocation = _responseParser.Parse(rawContent ?? string.Empty);
        if (invocation is null)
        {
            return BuildFailureResponse("AI output invalid or missing tool.", request.Model, trimmedMessages);
        }

        if (!_toolRegistry.TryValidate(invocation.Tool, invocation.Arguments, out var intent, out var validationError, out var requiresWrite))
        {
            return BuildFailureResponse(validationError ?? "Tool validation failed.", request.Model, trimmedMessages);
        }

        try
        {
            if (requiresWrite)
            {
                _rbacService.EnsureWriteAllowed(invocation.Tool, context);
            }
            else
            {
                _rbacService.EnsureReadAllowed(invocation.Tool, context);
            }
        }
        catch (SecurityException)
        {
            return BuildFailureResponse("Forbidden.", request.Model, trimmedMessages);
        }

        if (intent is OrderQueryIntent queryIntent && _contextManager.IsQueryOverlyBroad(queryIntent))
        {
            return BuildFailureResponse("Query too broad. Narrow the timeframe or scope.", request.Model, trimmedMessages);
        }

        var result = await _toolDispatcher.DispatchAsync(invocation.Tool, intent!, context, cancellationToken);
        if (result is null || !result.Success)
        {
            return BuildFailureResponse("Tool execution failed.", request.Model, trimmedMessages);
        }

        await _auditLogger.LogToolExecutionAsync(context, invocation.Tool, intent!, result.Data, cancellationToken);
        return BuildSuccessResponse(invocation.Tool, result, request.Model, trimmedMessages);
    }

    private ChatCompletionResponse BuildFailureResponse(string error, string? model, IReadOnlyCollection<ChatCompletionMessage> promptMessages)
    {
        var payload = JsonSerializer.Serialize(new { error }, _serializerOptions);
        return BuildResponse(model, payload, promptMessages);
    }

    private ChatCompletionResponse BuildSuccessResponse(string tool, ToolExecutionResult result, string? model, IReadOnlyCollection<ChatCompletionMessage> promptMessages)
    {
        var payload = JsonSerializer.Serialize(new
        {
            tool,
            result.Message,
            result.Data
        }, _serializerOptions);

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
}
