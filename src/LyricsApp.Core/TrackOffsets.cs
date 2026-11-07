using System.Text.Json;

namespace LyricsApp.Core;

/// <summary>
/// Time offsets the user set for individual songs, keyed by the normalized artist and title the player reports.
/// A song without an entry plays with no offset.
/// </summary>
public sealed class TrackOffsets
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string? path;
    private readonly Dictionary<string, double> offsets;

    private TrackOffsets(string? path, Dictionary<string, double> offsets)
    {
        this.path = path;
        this.offsets = offsets;
    }

    /// <summary>Kept in memory only; nothing is written.</summary>
    public static TrackOffsets InMemory() => new(null, new(StringComparer.Ordinal));

    /// <summary>Reads the file if it is there; a missing or broken file starts empty.</summary>
    public static TrackOffsets Load(string path)
    {
        var offsets = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path)) is { } loaded)
            {
                foreach (var (key, value) in loaded)
                    if (key.Length > 0 && double.IsFinite(value) && value != 0) offsets[key] = value;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken file must not stop the app; offsets start over.
        }
        return new TrackOffsets(path, offsets);
    }

    public int Count => offsets.Count;

    /// <summary>The saved offset in seconds, or 0.</summary>
    public double Get(TrackInfo? track) =>
        Key(track) is { } key && offsets.TryGetValue(key, out var value) ? value : 0;

    /// <summary>Saves the offset for this song; 0 removes it. Songs without a name are not stored.</summary>
    public void Set(TrackInfo? track, double seconds)
    {
        if (Key(track) is not { } key) return;
        var changed = seconds == 0 ? offsets.Remove(key) : !offsets.TryGetValue(key, out var old) || old != seconds;
        if (seconds != 0) offsets[key] = seconds;
        if (changed) Save();
    }

    internal static string? Key(TrackInfo? track)
    {
        if (track is null) return null;
        var title = TrackMatcher.Normalize(track.Title);
        if (title.Length == 0) return null;
        // Normalize strips punctuation, so " - " cannot appear inside either part.
        return $"{TrackMatcher.Normalize(track.Artist)} - {title}";
    }

    private void Save()
    {
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sorted = new SortedDictionary<string, double>(offsets, StringComparer.Ordinal);
            File.WriteAllText(path, JsonSerializer.Serialize(sorted, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
