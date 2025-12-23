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

    public IReadOnlyCollection<ChatCompletionMessage> PrepareMessages(IReadOnlyCollection<ChatCompletionMessage> messages)
    {
        var sanitized = messages
            .Where(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return _tokenBudget.TrimToBudget(sanitized);
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
}
