namespace TILSOFTAI.Orchestration.Chat.Localization;

public interface IChatTextLocalizer
{
    string Get(string key, params object[] args);
    void SetCulture(string? cultureName);
}

public static class ChatTextKeys
{
    public const string SystemPrompt = "SystemPrompt";
    public const string BlockTitleInsight = "BlockTitle_Insight";
    public const string BlockTitleInsightPreview = "BlockTitle_InsightPreview";
    public const string BlockTitleListPreview = "BlockTitle_ListPreview";
    public const string TableTruncationNote = "TableTruncationNote";
    public const string ToolNotAllowed = "ToolNotAllowed";
    public const string FallbackNoContent = "FallbackNoContent";
    public const string PreviousQueryHint = "PreviousQueryHint";
    public const string ErrorSchemaMetadataRequired = "Error_SchemaMetadataRequired";
    public const string ErrorInvalidPlan = "Error_InvalidPlan";
}
