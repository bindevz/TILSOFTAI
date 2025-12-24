using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Llm;

public interface IChatCompletionClient
{
    Task<string?> GetCompletionAsync(
        string? model,
        IEnumerable<ChatCompletionMessage> messages,
        string systemPrompt,
        int maxTokens,
        double temperature,
        CancellationToken cancellationToken);
}
