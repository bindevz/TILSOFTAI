using System.Text;

namespace TILSOFTAI.Orchestration.Chat;

public static class PromptPolicy
{
    public const string BasePrompt = """
You are an enterprise intent classification engine, not a chat assistant.
You must return ONLY valid JSON.
You must return exactly this structure:
{"tool":"<tool_name>","arguments":{...}}
Allowed keys: tool, arguments.
You must use predefined tools only.
You must NEVER generate SQL.
You must NEVER explain anything.
You must NEVER return free-form text, greetings, control tokens, or follow-up questions.
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
        builder.AppendLine("Tool guidance:");
        builder.AppendLine("- customers.search: find customer IDs by name/email query.");
        builder.AppendLine("- models.search: find model IDs by name/category.");
        builder.AppendLine("- orders.create.prepare: requires customerId + modelId (+ optional color, quantity). Creates confirmation only.");
        builder.AppendLine("- orders.create.commit: requires confirmationId. Finalizes after user confirmation.");

        return builder.ToString();
    }
}
