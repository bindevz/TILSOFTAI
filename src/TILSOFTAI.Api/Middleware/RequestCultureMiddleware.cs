using System.Globalization;
using Microsoft.Extensions.Options;
using TILSOFTAI.Configuration;

namespace TILSOFTAI.Api.Middleware;

public sealed class RequestCultureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AppSettings _settings;

    public RequestCultureMiddleware(RequestDelegate next, IOptions<AppSettings> settings)
    {
        _next = next;
        _settings = settings.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var cultureName = ResolveCulture(context);
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
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

        await _next(context);
    }

    private string ResolveCulture(HttpContext context)
    {
        var supported = _settings.Localization.SupportedCultures ?? Array.Empty<string>();
        var defaultCulture = string.IsNullOrWhiteSpace(_settings.Localization.DefaultCulture)
            ? "en"
            : _settings.Localization.DefaultCulture;

        var headerLang = context.Request.Headers["X-Lang"].ToString();
        var fromHeader = MatchSupportedCulture(headerLang, supported);
        if (!string.IsNullOrWhiteSpace(fromHeader))
            return fromHeader!;

        var acceptLang = context.Request.Headers.AcceptLanguage.ToString();
        if (!string.IsNullOrWhiteSpace(acceptLang))
        {
            foreach (var part in acceptLang.Split(','))
            {
                var token = part.Split(';', 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                var matched = MatchSupportedCulture(token, supported);
                if (!string.IsNullOrWhiteSpace(matched))
                    return matched!;
            }
        }

        return MatchSupportedCulture(defaultCulture, supported) ?? defaultCulture;
    }

    private static string? MatchSupportedCulture(string? candidate, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrWhiteSpace(candidate) || supported.Count == 0)
            return null;

        var trimmed = candidate.Trim();
        foreach (var s in supported)
        {
            if (string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase))
                return s;
        }

        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
        {
            var neutral = trimmed.Substring(0, dash);
            foreach (var s in supported)
            {
                if (string.Equals(s, neutral, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }

        return null;
    }
}
