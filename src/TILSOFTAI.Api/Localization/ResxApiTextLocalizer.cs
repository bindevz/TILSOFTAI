using System.Globalization;
using System.Resources;

namespace TILSOFTAI.Api.Localization;

public sealed class ResxApiTextLocalizer : IApiTextLocalizer
{
    private static readonly ResourceManager ResourceManager =
        new("TILSOFTAI.Api.Resources.ApiTexts", typeof(ResxApiTextLocalizer).Assembly);

    public string Get(string key, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var value = ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        if (args is { Length: > 0 })
            return string.Format(CultureInfo.CurrentCulture, value, args);
        return value;
    }
}
