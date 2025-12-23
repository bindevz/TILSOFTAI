using System.Text;

namespace TILSOFTAI.Orchestration.Chat;

public static class PromptPolicy
{
    public const string BasePrompt = """
You are an enterprise AI assistant.
You must return ONLY valid JSON.
You must use predefined tools only.
You must NEVER generate SQL.
You must NEVER explain anything.
If the request is unclear, choose the closest matching tool.
""";

    public static string BuildPrompt(IEnumerable<string> allowedTools)
    {
        var builder = new StringBuilder();
        builder.AppendLine(BasePrompt.Trim());
        builder.AppendLine("Allowed tools:");
        foreach (var tool in allowedTools)
        {
            builder.AppendLine($"- {tool}");
        }

        return builder.ToString();
    }
}
