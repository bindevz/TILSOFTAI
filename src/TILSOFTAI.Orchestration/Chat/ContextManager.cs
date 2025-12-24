using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ContextManager
{
    private readonly TokenBudget _tokenBudget;

    public ContextManager(TokenBudget tokenBudget)
    {
        _tokenBudget = tokenBudget;
    }

    public IReadOnlyCollection<ChatCompletionMessage> PrepareMessages(string userContent)
    {
        var userMessage = new ChatCompletionMessage
        {
            Role = "user",
            Content = userContent
        };

        return _tokenBudget.TrimToBudget(new[] { userMessage });
    }

    public IReadOnlyCollection<ChatCompletionMessage> PrepareMessages(IReadOnlyCollection<ChatCompletionMessage> incoming)
    {
        var filtered = incoming
            .Where(m => (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) || string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrWhiteSpace(m.Content))
            .ToArray();

        return _tokenBudget.TrimToBudget(filtered);
    }

    public bool IsQueryOverlyBroad(OrderQueryIntent intent)
    {
        if (!intent.StartDate.HasValue && !intent.EndDate.HasValue)
        {
            return true;
        }

        var upper = intent.EndDate ?? DateTimeOffset.UtcNow;
        var lower = intent.StartDate ?? upper.AddDays(-90);
        var days = (upper - lower).TotalDays;

        return intent.PageSize > 500 || days > 365;
    }

    public IReadOnlyCollection<TChunk> Chunk<TChunk>(IEnumerable<TChunk> source, int chunkSize)
    {
        var result = new List<TChunk>();
        var buffer = new List<TChunk>(chunkSize);
        foreach (var item in source)
        {
            buffer.Add(item);
            if (buffer.Count >= chunkSize)
            {
                result.AddRange(buffer);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            result.AddRange(buffer);
        }

        return result;
    }
}
