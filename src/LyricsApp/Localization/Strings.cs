using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace LyricsApp;

/// <summary>
/// User-facing text from Localization/Strings.resx (English) and Strings.tr.resx (Turkish).
/// The language follows the Windows display language: Turkish for tr*, English otherwise.
/// XAML uses <see cref="LocExtension"/>; code uses the properties below.
/// </summary>
public static class Strings
{
    // The build names the .resx resources after this class, which sits next to them.
    public static ResourceManager ResourceManager { get; } = new(typeof(Strings).FullName!, typeof(Strings).Assembly);

    /// <summary>The text for <paramref name="key"/> in the UI language; the key itself if it is missing.</summary>
    public static string Get(string key) => ResourceManager.GetString(key) ?? key;

    public static string WaitingForTrack => Get(nameof(WaitingForTrack));
    public static string StartPlaying => Get(nameof(StartPlaying));
    public static string Searching => Get(nameof(Searching));
    public static string Instrumental => Get(nameof(Instrumental));
    public static string NotFound => Get(nameof(NotFound));
    public static string NotFoundWithErrors => Get(nameof(NotFoundWithErrors));

    public static string MediaSessionFailed(string message) => Format(nameof(MediaSessionFailed), message);
    public static string OffsetSeconds(string seconds) => Format(nameof(OffsetSeconds), seconds);
    public static string LocalFolderSource(string fileName) => Format(nameof(LocalFolderSource), fileName);

    private static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary>XAML: <c>Text="{local:Loc Key}"</c> looks up <c>Key</c> in <see cref="Strings"/>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public LocExtension() : this(string.Empty)
    {
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
