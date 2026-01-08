namespace TILSOFTAI.Orchestration.Chat.Localization;

public enum ChatLanguage
{
    Vi,
    En
}

public static class ChatLanguageExtensions
{
    public static string ToIsoCode(this ChatLanguage lang) => lang == ChatLanguage.En ? "en" : "vi";

    public static ChatLanguage FromIsoCode(string? code)
    {
        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)) return ChatLanguage.En;
        return ChatLanguage.Vi;
    }
}
