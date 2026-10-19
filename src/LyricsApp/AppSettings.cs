using System.IO;
using System.Text.Json;
using LyricsApp.Core.Providers;

namespace LyricsApp;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricsApp");

    private static string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    public string LyricsFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Lyrics");

    /// <summary>
    /// Lyrics sources in search order: local, lrclib, musixmatch, genius, lyricsovh.
    /// Remove an id to turn that source off. Found lyrics are still saved to <see cref="LyricsFolder"/>.
    /// </summary>
    public List<string> EnabledProviders { get; set; } = [.. ProviderCatalog.DefaultOrder];

    public bool Topmost { get; set; }

    /// <summary>Added to the player position; positive shows lines earlier.</summary>
    public double OffsetSeconds { get; set; }

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 460;
    public double Height { get; set; } = 640;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall back to defaults; a broken file must not stop the app.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
