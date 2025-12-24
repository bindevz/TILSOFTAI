namespace TILSOFTAI.Orchestration.Chat;

public static class ConversationPromptPolicy
{
    public const string BasePrompt = """
You are a conversational assistant for internal ERP users.
Answer concisely in text.
Do not call tools or APIs.
Do not mention tool usage.
""";
}
