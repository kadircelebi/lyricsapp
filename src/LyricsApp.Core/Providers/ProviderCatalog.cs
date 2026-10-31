namespace LyricsApp.Core.Providers;

/// <summary>
/// Turns the provider ids from settings into the search chain, in the given order.
/// Owns the HttpClients it creates; dispose it when the app closes.
/// </summary>
/// <param name="dataFolder">Where providers may keep small state (the Musixmatch token). Null keeps it in memory.</param>
public sealed class ProviderCatalog(string? dataFolder = null) : IDisposable
{
    public const string Local = "local";
    public const string LrcLib = "lrclib";
    public const string Musixmatch = "musixmatch";
    public const string YouTubeMusic = "youtubemusic";
    public const string Genius = "genius";
    public const string LyricsOvh = "lyricsovh";

    public static IReadOnlyList<string> DefaultOrder { get; } = [Local, LrcLib, Musixmatch, YouTubeMusic, Genius, LyricsOvh];

    // What settings files saved before the seen list existed already knew about.
    private static readonly string[] FirstSources = [Local, LrcLib, Musixmatch, Genius, LyricsOvh];

    private readonly List<HttpClient> clients = [];

    public static bool IsKnown(string? id) => DefaultOrder.Contains(Canonical(id));

    /// <summary>
    /// A saved list turns off whatever it leaves out, so a source added in an update would stay off.
    /// Adds the sources missing from <paramref name="seen"/> at their default place; sources the
    /// user already saw and removed stay off. Null <paramref name="seen"/> means an older settings file.
    /// </summary>
    public static List<string> AddNewSources(IEnumerable<string?>? enabled, IEnumerable<string?>? seen)
    {
        if (enabled is null) return [.. DefaultOrder];

        var result = enabled.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToList();
        var seenIds = (seen ?? FirstSources).Select(Canonical).ToHashSet();
        for (var i = 0; i < DefaultOrder.Count; i++)
        {
            var id = DefaultOrder[i];
            if (seenIds.Contains(id) || result.Any(e => Canonical(e) == id)) continue;

            // After the nearest source that comes before it by default, or first when none is on.
            var previous = DefaultOrder.Take(i).Reverse().Select(p => result.FindIndex(e => Canonical(e) == p)).FirstOrDefault(index => index >= 0, -1);
            result.Insert(previous + 1, id);
        }
        return result;
    }

    /// <summary>
    /// Ids not in the list are off; unknown ids and repeats are ignored. Null means the default order.
    /// <paramref name="local"/> is used for "local" (pass null to leave it out).
    /// </summary>
    public IReadOnlyList<ILyricsProvider> Build(IEnumerable<string?>? ids, LocalFolderProvider? local)
    {
        var providers = new List<ILyricsProvider>();
        foreach (var id in (ids ?? DefaultOrder).Select(Canonical).Distinct())
        {
            ILyricsProvider? provider = id switch
            {
                Local => local,
                LrcLib => new LrcLibProvider(Own(LrcLibProvider.CreateHttpClient())),
                Musixmatch => new MusixmatchProvider(Own(MusixmatchProvider.CreateHttpClient()),
                    dataFolder is null ? null : Path.Combine(dataFolder, "musixmatch-token.txt")),
                YouTubeMusic => new YouTubeMusicProvider(Own(YouTubeMusicProvider.CreateHttpClient())),
                Genius =>new GeniusProvider(Own(GeniusProvider.CreateHttpClient())),
                LyricsOvh => new LyricsOvhProvider(Own(LyricsOvhProvider.CreateHttpClient())),
                _ => null,
            };
            if (provider is not null) providers.Add(provider);
        }
        return providers;
    }

    public void Dispose()
    {
        foreach (var client in clients) client.Dispose();
        clients.Clear();
    }

    private HttpClient Own(HttpClient client)
    {
        clients.Add(client);
        return client;
    }

    // "lyrics.ovh", "LyricsOvh" and " lrclib " all name a provider.
    private static string Canonical(string? id) =>
        new string((id ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
