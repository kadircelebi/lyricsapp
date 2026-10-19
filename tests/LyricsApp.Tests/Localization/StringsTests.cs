using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LyricsApp.Tests.Localization;

public partial class StringsTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    [Fact]
    public void English_and_Turkish_have_the_same_keys()
    {
        var english = Read(CultureInfo.InvariantCulture);
        var turkish = Read(Turkish);

        Assert.NotEmpty(english);
        Assert.Empty(english.Keys.Except(turkish.Keys));
        Assert.Empty(turkish.Keys.Except(english.Keys));
    }

    [Fact]
    public void Every_text_is_filled_in_and_keeps_its_placeholders()
    {
        var english = Read(CultureInfo.InvariantCulture);
        var turkish = Read(Turkish);

        foreach (var (key, text) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), key);
            Assert.False(string.IsNullOrWhiteSpace(turkish[key]), key);
            Assert.Equal(Placeholders(text), Placeholders(turkish[key]));
        }
    }

    [Theory]
    [InlineData("tr-TR", "Her zaman üstte")]
    [InlineData("tr", "Her zaman üstte")]
    [InlineData("en-US", "Always on top")]
    [InlineData("de-DE", "Always on top")]
    public void Language_follows_the_ui_culture(string culture, string expected) =>
        Assert.Equal(expected, Strings.ResourceManager.GetString("AlwaysOnTop", CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Every_key_used_in_xaml_exists()
    {
        var english = Read(CultureInfo.InvariantCulture);
        var xaml = Directory.GetFiles(AppSourceFolder(), "*.xaml");
        Assert.NotEmpty(xaml);

        var used = xaml.SelectMany(f => LocKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value))).ToList();
        Assert.NotEmpty(used);
        Assert.DoesNotContain(used, u => !english.ContainsKey(u.Key));
    }

    [Fact]
    public void Manifest_texts_exist_in_both_languages()
    {
        var folder = Path.Combine(AppSourceFolder(), "Localization");
        var english = ReadResw(Path.Combine(folder, "en-US", "Resources.resw"));
        var turkish = ReadResw(Path.Combine(folder, "tr-TR", "Resources.resw"));

        Assert.Contains("AppDescription", english.Keys);
        Assert.Equal(english.Keys.Order(), turkish.Keys.Order());
        Assert.DoesNotContain(english.Concat(turkish), e => string.IsNullOrWhiteSpace(e.Value));

        var manifest = File.ReadAllText(Path.Combine(AppSourceFolder(), "Package.appxmanifest"));
        foreach (Match m in MsResource().Matches(manifest)) Assert.Contains(m.Groups[1].Value, english.Keys);
    }

    private static Dictionary<string, string> Read(CultureInfo culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static Dictionary<string, string> ReadResw(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);

    private static string[] Placeholders(string text) => [.. Placeholder().Matches(text).Select(m => m.Value).Order()];

    // tests/LyricsApp.Tests/Localization/ -> src/LyricsApp/
    private static string AppSourceFolder([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "src", "LyricsApp"));

    [GeneratedRegex(@"\{local:Loc\s+(?:Key=)?(\w+)\s*\}")]
    private static partial Regex LocKey();

    [GeneratedRegex(@"ms-resource:(\w+)")]
    private static partial Regex MsResource();

    [GeneratedRegex(@"\{\d+(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();
}
