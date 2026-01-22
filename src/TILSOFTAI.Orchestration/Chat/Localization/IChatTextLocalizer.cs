namespace TILSOFTAI.Orchestration.Chat.Localization;

public interface IChatTextLocalizer
{
    string Get(string key, ChatLanguage lang);
}

public static class ChatTextKeys
{
    public const string SystemPrompt = "system_prompt";
    public const string FallbackNoContent = "fallback_no_content";
    public const string InsightBlockTitle = "insight_block_title";
    public const string InsightPreviewTitle = "insight_preview_title";
    public const string ListPreviewTitle = "list_preview_title";
    public const string TableTruncationNote = "table_truncation_note";
}
