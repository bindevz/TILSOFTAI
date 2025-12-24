using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class TokenBudget
{
    public TokenBudget(int maxTokens = 8000, int reservedResponseTokens = 1000)
    {
        MaxTokens = maxTokens;
        ReservedResponseTokens = reservedResponseTokens;
    }

    public int MaxTokens { get; }
    public int ReservedResponseTokens { get; }

    public int EstimateMessageTokens(IEnumerable<ChatCompletionMessage> messages)
    {
        return messages.Sum(message => EstimateTokens(message.Content));
    }

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, text.Length / 4);
    }

    public IReadOnlyCollection<ChatCompletionMessage> TrimToBudget(IReadOnlyCollection<ChatCompletionMessage> messages)
    {
        var result = new List<ChatCompletionMessage>(messages);
        while (EstimateMessageTokens(result) + ReservedResponseTokens > MaxTokens && result.Count > 1)
        {
            result.RemoveAt(0);
        }

        return result;
    }

    public void EnsureWithinBudget(string content)
    {
        var tokens = EstimateTokens(content);
        if (tokens + ReservedResponseTokens > MaxTokens)
        {
            throw new ResponseContractException("Token budget exceeded.");
        }
    }
}
