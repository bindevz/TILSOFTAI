namespace TILSOFTAI.Orchestration.Chat.Localization;

public interface IChatTextLocalizer
{
    string Get(string key, ChatLanguage lang);
}

public static class ChatTextKeys
{
    public const string SystemPrompt = "system_prompt";
    public const string PreviousQueryHint = "previous_query_hint";
    public const string FallbackNoContent = "fallback_no_content";
}
