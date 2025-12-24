using System.Security;
using System.Text.Json;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools;
using ExecutionContext = TILSOFTAI.Domain.ValueObjects.ExecutionContext;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ChatPipeline
{
    private readonly IChatCompletionClient _chatClient;
    private readonly ResponseParser _responseParser;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly ContextManager _contextManager;
    private readonly TokenBudget _tokenBudget;
    private readonly IAuditLogger _auditLogger;
    private readonly RbacService _rbacService;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public ChatPipeline(
        IChatCompletionClient chatClient,
        ResponseParser responseParser,
        ToolRegistry toolRegistry,
        ToolDispatcher toolDispatcher,
        ContextManager contextManager,
        TokenBudget tokenBudget,
        IAuditLogger auditLogger,
        RbacService rbacService)
    {
        _chatClient = chatClient;
        _responseParser = responseParser;
        _toolRegistry = toolRegistry;
        _toolDispatcher = toolDispatcher;
        _contextManager = contextManager;
        _tokenBudget = tokenBudget;
        _auditLogger = auditLogger;
        _rbacService = rbacService;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, ExecutionContext context, CancellationToken cancellationToken)
    {
        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);

        var incomingMessages = request.Messages ?? Array.Empty<ChatCompletionMessage>(); // 1. Receive user input
        if (incomingMessages.Any(m => !IsSupportedRole(m.Role)))
        {
            return BuildFailureResponse("Unsupported message role.", request.Model, Array.Empty<ChatCompletionMessage>());
        }

        var hasUser = incomingMessages.Any(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content));
        if (!hasUser)
        {
            return BuildFailureResponse("User message required.", request.Model, Array.Empty<ChatCompletionMessage>());
        }

        var lastUserMessage = incomingMessages.Last(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content)).Content!;

        var promptMessages = _contextManager.PrepareMessages(incomingMessages);

        var systemPrompt = PromptPolicy.BuildPrompt(_toolRegistry.GetToolNames()); // 2. Inject system prompt

        string rawContent;
        try
        {
            rawContent = await _chatClient.GetCompletionAsync(request.Model, promptMessages, systemPrompt, 256, 0.0, cancellationToken)
                ?? throw new ResponseContractException("AI response was empty."); // 3. Call LLM
        }
        catch (Exception)
        {
            return BuildFailureResponse("AI response unavailable.", request.Model, promptMessages);
        }

        ToolInvocation invocation;
        try
        {
            invocation = _responseParser.Parse(rawContent); // 4. Strict JSON parsing
        }
        catch (ResponseContractException)
        {
            return BuildFailureResponse("AI output invalid.", request.Model, promptMessages);
        }

        if (!_toolRegistry.IsWhitelisted(invocation.Tool)) // 5. Tool whitelist validation
        {
            return BuildFailureResponse("Tool not allowed.", request.Model, promptMessages);
        }

        if (!_toolRegistry.TryValidate(invocation.Tool, invocation.Arguments, out var intent, out var validationError, out var requiresWrite)) // 6. JSON schema validation
        {
            return BuildFailureResponse(validationError ?? "Invalid arguments.", request.Model, promptMessages);
        }

        try // 7. RBAC validation
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
            return BuildFailureResponse("Forbidden.", request.Model, promptMessages);
        }

        ToolDispatchResult dispatchResult;
        try
        {
            dispatchResult = await _toolDispatcher.DispatchAsync(invocation.Tool, intent!, context, requiresWrite, cancellationToken); // 8-9. Business context normalization + execution
        }
        catch (ResponseContractException)
        {
            return BuildFailureResponse("Normalization failed.", request.Model, promptMessages);
        }

        await _auditLogger.LogUserInputAsync(context, serializedRequest, cancellationToken); // 10. Audit logging
        await _auditLogger.LogAiDecisionAsync(context, rawContent, cancellationToken);
        await _auditLogger.LogToolExecutionAsync(context, invocation.Tool, dispatchResult.NormalizedIntent, new { dispatchResult.Result.Success, dispatchResult.Result.Message, dispatchResult.Result.Data }, cancellationToken);

        if (!dispatchResult.Result.Success)
        {
            return BuildFailureResponse("Tool execution failed.", request.Model, promptMessages);
        }

        var answerMessages = BuildAnswerMessages(lastUserMessage, invocation, dispatchResult);
        var answerContent = await GetAnswerAsync(request.Model, answerMessages, cancellationToken);

        if (string.IsNullOrWhiteSpace(answerContent))
        {
            var fallback = BuildSuccessResponse(invocation.Tool, dispatchResult.Result, request.Model, answerMessages);
            _tokenBudget.EnsureWithinBudget(fallback.Choices.First().Message.Content);
            return fallback;
        }

        var naturalResponse = BuildResponse(request.Model, answerContent, answerMessages);
        _tokenBudget.EnsureWithinBudget(naturalResponse.Choices.First().Message.Content);
        return naturalResponse;
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

    private async Task<string?> GetAnswerAsync(string? model, IReadOnlyCollection<ChatCompletionMessage> messages, CancellationToken cancellationToken)
    {
        try
        {
            return await _chatClient.GetCompletionAsync(model, messages, AnswerPromptPolicy.BasePrompt, 512, 0.2, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyCollection<ChatCompletionMessage> BuildAnswerMessages(string lastUserMessage, ToolInvocation invocation, ToolDispatchResult dispatchResult)
    {
        var evidence = new
        {
            tool = invocation.Tool,
            normalizedIntent = dispatchResult.NormalizedIntent,
            result = new
            {
                message = dispatchResult.Result.Message,
                data = dispatchResult.Result.Data
            }
        };

        var evidenceJson = JsonSerializer.Serialize(evidence, _serializerOptions);
        var confirmationId = ExtractConfirmationId(dispatchResult.Result.Data);

        var userPrompt = confirmationId is null
            ? lastUserMessage
            : $"{lastUserMessage}\nPlease ask for confirmation using confirmation id: {confirmationId}.";

        return new[]
        {
            new ChatCompletionMessage { Role = "user", Content = userPrompt },
            new ChatCompletionMessage { Role = "assistant", Content = $"EVIDENCE: {evidenceJson}" }
        };
    }

    private string? ExtractConfirmationId(object? data)
    {
        if (data is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data, _serializerOptions));
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(doc.RootElement, "confirmationId", out var id) ||
                    TryGetProperty(doc.RootElement, "confirmation_id", out id))
                {
                    return id;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var text = prop.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }

        return false;
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

    private static bool IsSupportedRole(string? role)
    {
        return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "system", StringComparison.OrdinalIgnoreCase);
    }
}
