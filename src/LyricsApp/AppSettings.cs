using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LyricsApp.Core.Providers;

namespace LyricsApp;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>%APPDATA%\LyricsApp, or LYRICSAPP_SETTINGS_DIR when set (handy for testing without touching real settings).</summary>
    public static string SettingsFolder { get; } =
        Environment.GetEnvironmentVariable("LYRICSAPP_SETTINGS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricsApp");

    private static string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    /// <summary>Per-song time offsets (see <see cref="LyricsApp.Core.TrackOffsets"/>).</summary>
    public static string OffsetsPath => Path.Combine(SettingsFolder, "offsets.json");

    public string LyricsFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Lyrics");

    /// <summary>
    /// Lyrics sources in search order: local, lrclib, musixmatch, youtubemusic, genius, lyricsovh.
    /// Remove an id to turn that source off. Found lyrics are still saved to <see cref="LyricsFolder"/>.
    /// </summary>
    public List<string> EnabledProviders { get; set; } = [.. ProviderCatalog.DefaultOrder];

    /// <summary>Every source this file has been offered, so one added by an update gets switched on once.</summary>
    public List<string>? SeenProviders { get; set; }

    /// <summary>
    /// When no source has the lyrics, listen to the player for a few seconds and ask Shazam for the real song name.
    /// On by default; the settings window explains what is sent.
    /// </summary>
    public bool RecognizeUnfoundSongs { get; set; } = true;

    public DisplaySettings Display { get; set; } = new();

    /// <summary>Old location of the always-on-top flag (v0.1/v0.2); read once and moved into <see cref="Display"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Topmost { get; set; }

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 460;
    public double Height { get; set; } = 640;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
                loaded.Display ??= new DisplaySettings();
                if (loaded.Topmost)
                {
                    loaded.Display.Topmost = true;
                    loaded.Topmost = false;
                }
                loaded.EnabledProviders = ProviderCatalog.AddNewSources(loaded.EnabledProviders, loaded.SeenProviders);
                loaded.SeenProviders = [.. ProviderCatalog.DefaultOrder];
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall back to defaults; a broken file must not stop the app.
        }
        return new AppSettings { SeenProviders = [.. ProviderCatalog.DefaultOrder] };
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
