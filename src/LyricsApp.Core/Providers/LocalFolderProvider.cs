namespace LyricsApp.Core.Providers;

/// <summary>
/// Looks for "Artist - Title.lrc" (or .txt) in a folder and its subfolders.
/// Also serves as the cache: lyrics found online are saved here.
/// </summary>
public sealed class LocalFolderProvider(string folder) : ILyricsProvider
{
    private static readonly EnumerationOptions SearchOptions = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };

    /// <summary>Provider name, also the start of <see cref="Lyrics.Source"/> ("Local folder: file.lrc"). The UI translates it.</summary>
    public const string SourceName = "Local folder";

    public string Name => SourceName;

    public string Folder { get; } = folder;

    public static string GetFileStem(TrackInfo track)
    {
        var stem = string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Artist} - {track.Title}";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(stem.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length > 150 ? cleaned[..150] : cleaned;
    }

    public async Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        if (track.IsEmpty || !Directory.Exists(Folder)) return null;

        var stem = GetFileStem(track);
        var files = new DirectoryInfo(Folder).EnumerateFiles(stem + ".*", SearchOptions)
            .Where(f => f.Extension.Equals(".lrc", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Extension.Equals(".lrc", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var source = $"{Name}: {file.Name}";
            if (LrcParser.IsLrc(text))
            {
                var lines = LrcParser.Parse(text);
                if (lines.Count > 0) return new LyricsResult(new SyncedLyrics(lines, source), track);
            }
            return new LyricsResult(new PlainLyrics(text.Trim(), source), track);
        }
        return null;
    }

    /// <summary>Saves lyrics as .lrc (synced) or .txt (plain). Returns the written path, or null if nothing was saved.</summary>
    public async Task<string?> SaveAsync(TrackInfo track, Lyrics lyrics, CancellationToken cancellationToken)
    {
        if (track.IsEmpty || lyrics is InstrumentalLyrics) return null;

        Directory.CreateDirectory(Folder);
        var extension = lyrics is SyncedLyrics ? ".lrc" : ".txt";
        var path = Path.Combine(Folder, GetFileStem(track) + extension);
        await File.WriteAllTextAsync(path, lyrics.ToText(), cancellationToken);
        return path;
    }
}
