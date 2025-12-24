using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class LmStudioChatCompletionClient : IChatCompletionClient
{
    private readonly LmStudioClient _client;

    public LmStudioChatCompletionClient(LmStudioClient client)
    {
        _client = client;
    }

    public Task<string?> GetCompletionAsync(
        string? model,
        IEnumerable<ChatCompletionMessage> messages,
        string systemPrompt,
        int maxTokens,
        double temperature,
        CancellationToken cancellationToken)
    {
        return _client.GetToolIntentAsync(model, messages, systemPrompt, cancellationToken);
    }
}
