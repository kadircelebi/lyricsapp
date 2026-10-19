using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LyricsApp.Core.Providers;

/// <summary>
/// https://lrclib.net — free, open lyrics database with line-synced lyrics.
/// Asks for an exact match first (title + artist + album + duration), then falls back to search.
/// </summary>
public sealed class LrcLibProvider(HttpClient http) : ILyricsProvider
{
    public const string BaseAddress = "https://lrclib.net/api/";

    public string Name => "LrcLib";

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromSeconds(10) };
        // LrcLib asks clients to identify themselves.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LyricsApp/0.1 (personal desktop lyrics viewer)");
        return client;
    }

    public async Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        if (track.IsEmpty) return null;

        // 1) Exact signature match. Requires all four fields; duration must be within ±2 s on the server.
        if (!string.IsNullOrWhiteSpace(track.Artist) && !string.IsNullOrWhiteSpace(track.Album) && track.Duration is { TotalSeconds: > 0 } d)
        {
            var url = $"get?track_name={Q(track.Title)}&artist_name={Q(track.Artist)}&album_name={Q(track.Album)}&duration={(int)Math.Round(d.TotalSeconds)}";
            using var exact = await GetJsonAsync(url, cancellationToken);
            if (exact is not null && ToResult(exact.RootElement) is { } hit && TrackMatcher.IsAcceptable(track, hit.MatchedTrack))
                return hit;
        }

        // 2) Field search, then free-text search; pick the best acceptable candidate.
        var queries = new List<string>();
        if (!string.IsNullOrWhiteSpace(track.Artist))
            queries.Add($"search?track_name={Q(track.Title)}&artist_name={Q(track.Artist)}");
        queries.Add($"search?q={Q($"{track.Artist} {track.Title}".Trim())}");

        foreach (var url in queries)
        {
            using var doc = await GetJsonAsync(url, cancellationToken);
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) continue;

            var best = PickBest(track, doc.RootElement);
            if (best is not null) return best;
        }
        return null;
    }

    internal static LyricsResult? PickBest(TrackInfo query, JsonElement results)
    {
        LyricsResult? best = null;
        var bestScore = int.MinValue;

        foreach (var item in results.EnumerateArray())
        {
            if (ToResult(item) is not { } candidate) continue;

            var score = TrackMatcher.Score(query, candidate.MatchedTrack);
            if (score < TrackMatcher.MinAcceptableScore) continue;

            // Prefer synced lyrics among equally good matches.
            if (candidate.Lyrics is SyncedLyrics) score += 1;
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    internal static LyricsResult? ToResult(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        var matched = new TrackInfo(
            GetString(item, "artistName") ?? string.Empty,
            GetString(item, "trackName") ?? string.Empty,
            GetString(item, "albumName"),
            item.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number
                ? TimeSpan.FromSeconds(dur.GetDouble())
                : null);

        var id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt64().ToString(CultureInfo.InvariantCulture)
            : null;
        var source = id is null ? "LrcLib" : $"LrcLib #{id}";

        if (item.TryGetProperty("instrumental", out var instr) && instr.ValueKind == JsonValueKind.True)
            return new LyricsResult(new InstrumentalLyrics(source), matched);

        var synced = GetString(item, "syncedLyrics");
        if (LrcParser.IsLrc(synced))
        {
            var lines = LrcParser.Parse(synced!);
            if (lines.Count > 0) return new LyricsResult(new SyncedLyrics(lines, source), matched);
        }

        var plain = GetString(item, "plainLyrics");
        return string.IsNullOrWhiteSpace(plain) ? null : new LyricsResult(new PlainLyrics(plain.Trim(), source), matched);
    }

    // LrcLib intermittently answers 5xx under load; a quick retry usually succeeds.
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200)];

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await http.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if ((int)response.StatusCode >= 500 && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt], cancellationToken);
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static string Q(string? value) => Uri.EscapeDataString(value ?? string.Empty);
}
