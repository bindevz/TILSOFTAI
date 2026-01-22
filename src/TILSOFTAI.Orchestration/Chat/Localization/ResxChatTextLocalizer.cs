using System.Globalization;
using System.Resources;

namespace TILSOFTAI.Orchestration.Chat.Localization;

public sealed class ResxChatTextLocalizer : IChatTextLocalizer
{
    private static readonly ResourceManager ResourceManager =
        new("TILSOFTAI.Orchestration.Resources.ChatTexts", typeof(ResxChatTextLocalizer).Assembly);

    public string Get(string key, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var culture = CultureInfo.CurrentUICulture;
        var template = ResourceManager.GetString(key, culture) ?? key;

        if (args is null || args.Length == 0)
            return template;

        return string.Format(CultureInfo.CurrentCulture, template, args);
    }

    public void SetCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Ignore invalid culture names.
        }
    }
}
