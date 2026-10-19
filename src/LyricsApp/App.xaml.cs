using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace LyricsApp;

public partial class App : Application
{
    public App()
    {
        // The UI language follows Windows (Strings.tr.resx for tr*, English otherwise).
        // LYRICSAPP_LANGUAGE=en or tr forces one, for testing and Store screenshots.
        if (Environment.GetEnvironmentVariable("LYRICSAPP_LANGUAGE") is { Length: > 0 } language)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(language);
                CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
            }
        }

        // WPF formats bound numbers (percentages, sizes) as en-US unless told the culture.
        // Default values are not inherited, so Runs inside a TextBlock need their own.
        var xmlLanguage = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(xmlLanguage));
        FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.Documents.TextElement), new FrameworkPropertyMetadata(xmlLanguage));
    }
}
