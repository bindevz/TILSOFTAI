using System.Globalization;
using System.Resources;
using TILSOFTAI.Orchestration.Chat.Localization;
using Xunit;

namespace TILSOFTAI.Orchestration.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void ResxChatTextLocalizer_Resolves_En_And_Vi()
    {
        var localizer = new ResxChatTextLocalizer();
        var manager = new ResourceManager("TILSOFTAI.Orchestration.Resources.ChatTexts", typeof(ResxChatTextLocalizer).Assembly);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var en = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = en;
            CultureInfo.CurrentUICulture = en;
            var enValue = localizer.Get(ChatTextKeys.BlockTitleInsight);
            var enExpected = manager.GetString(ChatTextKeys.BlockTitleInsight, en);
            Assert.Equal(enExpected, enValue);

            var vi = CultureInfo.GetCultureInfo("vi-VN");
            CultureInfo.CurrentCulture = vi;
            CultureInfo.CurrentUICulture = vi;
            var viValue = localizer.Get(ChatTextKeys.BlockTitleInsight);
            var viExpected = manager.GetString(ChatTextKeys.BlockTitleInsight, vi);
            Assert.Equal(viExpected, viValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ResxChatTextLocalizer_Formats_Placeholders()
    {
        var localizer = new ResxChatTextLocalizer();
        var manager = new ResourceManager("TILSOFTAI.Orchestration.Resources.ChatTexts", typeof(ResxChatTextLocalizer).Assembly);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var template = manager.GetString(ChatTextKeys.TableTruncationNote, culture) ?? string.Empty;
            var expected = string.Format(culture, template, 1, 2);
            var actual = localizer.Get(ChatTextKeys.TableTruncationNote, 1, 2);

            Assert.Equal(expected, actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
