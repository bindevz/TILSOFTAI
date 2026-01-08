namespace TILSOFTAI.Orchestration.Chat.Localization;

public interface IChatTextLocalizer
{
    string Get(string key, ChatLanguage lang);
}

public static class ChatTextKeys
{
    public const string SystemPrompt = "system_prompt";
    public const string FeatureNotAvailable = "feature_not_available";
    public const string NoToolsMode = "no_tools_mode";
    public const string SynthesizeNoTools = "synthesize_no_tools";
    public const string PreviousQueryHint = "previous_query_hint";
}
