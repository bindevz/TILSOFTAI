using System.Text.Json;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Llm;
using ExecutionContext = TILSOFTAI.Domain.ValueObjects.ExecutionContext;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ConversationPipeline
{
    private readonly LmStudioClient _lmStudioClient;
    private readonly TokenBudget _tokenBudget;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public ConversationPipeline(LmStudioClient lmStudioClient, TokenBudget tokenBudget)
    {
        _lmStudioClient = lmStudioClient;
        _tokenBudget = tokenBudget;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, ExecutionContext context, CancellationToken cancellationToken)
    {
        var incoming = request.Messages?.ToArray() ?? Array.Empty<ChatCompletionMessage>();
        if (incoming.Length != 1 || !string.Equals(incoming[0].Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Conversation mode requires a single user message.");
        }

        var resolvedModel = request.Model ?? "tilsoftai-conversation";
        var messages = new[]
        {
            new ChatCompletionMessage { Role = "system", Content = ConversationPromptPolicy.BasePrompt },
            incoming[0]
        };

        var content = await _lmStudioClient.GetConversationAsync(resolvedModel, messages, cancellationToken)
            ?? throw new InvalidOperationException("Empty response.");

        var completionTokens = _tokenBudget.EstimateTokens(content);
        var promptTokens = _tokenBudget.EstimateMessageTokens(messages);

        var response = new ChatCompletionResponse
        {
            Id = $"chatcmpl-{Guid.NewGuid():N}",
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = resolvedModel,
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

        _tokenBudget.EnsureWithinBudget(response.Choices.First().Message.Content);
        return response;
    }
}
