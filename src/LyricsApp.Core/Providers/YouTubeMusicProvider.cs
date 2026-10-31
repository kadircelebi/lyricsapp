using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static LyricsApp.Core.Providers.ProviderHelpers;

namespace LyricsApp.Core.Providers;

/// <summary>
/// YouTube Music through the unofficial API its own clients use (no account). Only its Android client
/// gets timed lines, so that request poses as the app. Against their terms and may break at any time.
/// Order: song search → lyrics page id → timed lines → plain lyrics.
/// </summary>
public sealed partial class YouTubeMusicProvider(HttpClient http) : ILyricsProvider
{
    public const string BaseAddress = "https://music.youtube.com/youtubei/v1/";

    // The search page's "Songs" filter.
    private const string SongsFilter = "EgWKAQIIAWoMEA4QChADEAQQCRAF";

    [GeneratedRegex(@"^(?:(\d+):)?(\d{1,2}):(\d{2})$")]
    private static partial Regex DurationRegex();

    public string Name => "YouTube Music";

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Origin", "https://music.youtube.com");
        return client;
    }

    internal sealed record YtSong(string VideoId, TrackInfo Info);

    public async Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        if (track.IsEmpty) return null;

        YtSong? song;
        using (var search = await PostAsync("search", WebClient(), new JsonObject
        {
            ["query"] = $"{track.Artist} {track.Title}".Trim(),
            ["params"] = SongsFilter,
        }, cancellationToken))
        {
            song = PickBest(track, search.RootElement);
        }
        if (song is null) return null;

        string? browseId;
        using (var next = await PostAsync("next", WebClient(), new JsonObject
        {
            ["videoId"] = song.VideoId,
            ["isAudioOnly"] = true,
        }, cancellationToken))
        {
            browseId = FindLyricsBrowseId(next.RootElement);
        }
        if (browseId is null) return null;

        var source = $"YouTube Music: {song.VideoId}";
        using (var timed = await PostAsync("browse", AndroidClient(), new JsonObject { ["browseId"] = browseId }, cancellationToken))
        {
            var lines = ReadTimedLyrics(timed.RootElement);
            if (lines.Count > 0) return new LyricsResult(new SyncedLyrics(lines, source), song.Info);
        }

        using var page = await PostAsync("browse", WebClient(), new JsonObject { ["browseId"] = browseId }, cancellationToken);
        return ReadPlainLyrics(page.RootElement) is { } plain ? new LyricsResult(new PlainLyrics(plain, source), song.Info) : null;
    }

    /// <summary>Picks from every <c>musicResponsiveListItemRenderer</c> in the search answer.</summary>
    internal static YtSong? PickBest(TrackInfo query, JsonElement root)
    {
        YtSong? best = null;
        var bestScore = int.MinValue;
        foreach (var item in FindAll(root, "musicResponsiveListItemRenderer"))
        {
            if (ReadSong(item) is not { } candidate) continue;

            var score = TrackMatcher.Score(query, candidate.Info);
            if (score < TrackMatcher.MinAcceptableScore || score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    /// <summary>
    /// The first column holds the title. The second holds "artists • album • m:ss", where
    /// several artists are separate runs joined by " ve " or " &amp; ".
    /// </summary>
    internal static YtSong? ReadSong(JsonElement item)
    {
        if (!item.TryGetProperty("flexColumns", out var columns) || columns.ValueKind != JsonValueKind.Array || columns.GetArrayLength() < 2) return null;

        var title = string.Concat(ColumnRuns(columns[0]).Select(r => GetString(r, "text")));
        var sections = new List<StringBuilder> { new() };
        foreach (var run in ColumnRuns(columns[1]))
        {
            var text = GetString(run, "text") ?? string.Empty;
            if (text.Trim() == "•") sections.Add(new StringBuilder());
            else sections[^1].Append(text);
        }

        var artist = sections[0].ToString().Trim();
        var videoId = FindAll(item, "videoId").Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : null).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (videoId is null || string.IsNullOrWhiteSpace(title) || artist.Length == 0) return null;

        var duration = sections.Count > 1 ? ParseDuration(sections[^1].ToString().Trim()) : null;
        var album = sections.Count > 2 ? sections[1].ToString().Trim() : null;
        return new YtSong(videoId, new TrackInfo(artist, title.Trim(), album, duration));
    }

    /// <summary>The song page links its lyrics as a browse id starting with MPLY; no such id means no lyrics.</summary>
    internal static string? FindLyricsBrowseId(JsonElement root) =>
        FindAll(root, "browseId")
            .Select(b => b.ValueKind == JsonValueKind.String ? b.GetString() : null)
            .FirstOrDefault(b => b?.StartsWith("MPLY", StringComparison.Ordinal) == true);

    /// <summary><c>timedLyricsData</c>: [{ "lyricLine": "text", "cueRange": { "startTimeMilliseconds": "1234" } }]. A lone ♪ marks a pause.</summary>
    internal static IReadOnlyList<LyricLine> ReadTimedLyrics(JsonElement root)
    {
        var data = FindAll(root, "timedLyricsData").FirstOrDefault(d => d.ValueKind == JsonValueKind.Array);
        if (data.ValueKind != JsonValueKind.Array) return [];

        var lines = new List<LyricLine>();
        foreach (var entry in data.EnumerateArray())
        {
            if (!TryGetPath(entry, out var cue, "cueRange") || GetInt64(cue, "startTimeMilliseconds") is not { } start) continue;
            var text = (GetString(entry, "lyricLine") ?? string.Empty).Trim();
            lines.Add(new LyricLine(TimeSpan.FromMilliseconds(start), text.Trim('♪', ' ').Length == 0 ? string.Empty : text));
        }
        return lines.Any(l => l.Text.Length > 0) ? lines.OrderBy(l => l.Time).ToList() : [];
    }

    /// <summary><c>musicDescriptionShelfRenderer.description.runs[].text</c>.</summary>
    internal static string? ReadPlainLyrics(JsonElement root)
    {
        foreach (var shelf in FindAll(root, "musicDescriptionShelfRenderer"))
        {
            if (!TryGetPath(shelf, out var runs, "description", "runs") || runs.ValueKind != JsonValueKind.Array) continue;
            var text = NormalizePlainText(string.Concat(runs.EnumerateArray().Select(r => GetString(r, "text"))));
            if (text.Length > 0) return text;
        }
        return null;
    }

    private static TimeSpan? ParseDuration(string text)
    {
        var m = DurationRegex().Match(text);
        if (!m.Success) return null;
        var hours = m.Groups[1].Success ? int.Parse(m.Groups[1].ValueSpan) : 0;
        return new TimeSpan(hours, int.Parse(m.Groups[2].ValueSpan), int.Parse(m.Groups[3].ValueSpan));
    }

    private static IEnumerable<JsonElement> ColumnRuns(JsonElement column) =>
        TryGetPath(column, out var runs, "musicResponsiveListItemFlexColumnRenderer", "text", "runs") && runs.ValueKind == JsonValueKind.Array
            ? runs.EnumerateArray()
            : [];

    // These answers are deeply nested and their layout shifts, so fields are found by name wherever they are.
    private static IEnumerable<JsonElement> FindAll(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name)) yield return property.Value;
                foreach (var found in FindAll(property.Value, name)) yield return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var found in FindAll(child, name)) yield return found;
            }
        }
    }

    private static JsonObject WebClient() => new()
    {
        ["clientName"] = "WEB_REMIX",
        ["clientVersion"] = "1.20250101.01.00",
        ["hl"] = "tr",
        ["gl"] = "TR",
    };

    private static JsonObject AndroidClient() => new()
    {
        ["clientName"] = "ANDROID_MUSIC",
        ["clientVersion"] = "7.11.50",
        ["androidSdkVersion"] = 30,
        ["hl"] = "tr",
        ["gl"] = "TR",
    };

    private async Task<JsonDocument> PostAsync(string endpoint, JsonObject client, JsonObject body, CancellationToken cancellationToken)
    {
        body["context"] = new JsonObject { ["client"] = client };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{endpoint}?prettyPrint=false", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
