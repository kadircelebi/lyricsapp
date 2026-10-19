using System.Net;
using System.Text;
using System.Text.Json;
using static LyricsApp.Core.Providers.ProviderHelpers;

namespace LyricsApp.Core.Providers;

/// <summary>
/// Musixmatch through its unofficial mobile API (poses as the iOS app). Best coverage for
/// Turkish songs, but it is against their terms and may break at any time.
/// Order: matcher (macro call) or search → richsync (word timings) → line subtitles → plain lyrics.
/// </summary>
/// <param name="tokenFile">Optional file that keeps the user token across restarts; token.get is rate limited.</param>
public sealed class MusixmatchProvider(HttpClient http, string? tokenFile = null) : ILyricsProvider
{
    public const string BaseAddress = "https://apic-appmobile.musixmatch.com/ws/1.1/";
    private const string AppId = "mac-ios-v2.0";

    // A pause this long between richsync lines gets an empty line, so the last line does not linger.
    private static readonly TimeSpan GapForBlankLine = TimeSpan.FromSeconds(5);

    // After token.get is refused (usually "captcha"), asking again right away only extends the block.
    private static readonly TimeSpan TokenCooldown = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTime tokenBlockedUntil = DateTime.MinValue;

    public string Name => "Musixmatch";

    public static HttpClient CreateHttpClient()
    {
        // Cookies are sent by hand below, so the handler must not manage them.
        var handler = new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Musixmatch/2025120901 CFNetwork/1568.300.101 Darwin/24.2.0");
        client.DefaultRequestHeaders.Add("Cookie", $"x-mxm-token-guid={Guid.NewGuid()}");
        return client;
    }

    internal sealed record MxmTrack(long Id, TrackInfo Info, bool Instrumental, bool HasLyrics, bool HasSubtitles, bool HasRichsync);

    public async Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        if (track.IsEmpty) return null;

        // 1) One macro call does the matching and returns line subtitles and plain lyrics with it.
        var macroQuery = $"namespace=lyrics_richsynched&subtitle_format=lrc&q_artist={Q(track.Artist)}&q_track={Q(track.Title)}";
        if (!string.IsNullOrWhiteSpace(track.Album)) macroQuery += $"&q_album={Q(track.Album)}";
        if (track.Duration is { TotalSeconds: > 0 } d) macroQuery += $"&q_duration={(int)Math.Round(d.TotalSeconds)}";

        using (var macro = await CallAsync("macro.subtitles.get", macroQuery, cancellationToken))
        {
            if (TryGetPath(macro.RootElement, out var calls, "message", "body", "macro_calls")
                && MacroBody(calls, "matcher.track.get") is { } matcherBody
                && TryGetPath(matcherBody, out var trackEl, "track")
                && ReadTrack(trackEl) is { } matched
                && TrackMatcher.IsAcceptable(track, matched.Info))
            {
                var subtitle = MacroBody(calls, "track.subtitles.get") is { } sb ? ReadSubtitle(sb) : null;
                var plain = MacroBody(calls, "track.lyrics.get") is { } lb ? ReadLyricsBody(lb) : null;
                if (await FetchAsync(matched, subtitle, plain, cancellationToken) is { } result) return result;
            }
        }

        // 2) Search and pick the best acceptable candidate ourselves.
        var searchQuery = $"q_artist={Q(track.Artist)}&q_track={Q(track.Title)}&f_has_lyrics=true&page_size=10&s_track_rating=desc";
        using var search = await CallAsync("track.search", searchQuery, cancellationToken);
        if (!TryGetPath(search.RootElement, out var list, "message", "body", "track_list")) return null;
        return PickBest(track, list) is { } best ? await FetchAsync(best, null, null, cancellationToken) : null;
    }

    /// <summary>Best lyrics for an identified track. Already known subtitle/plain text saves a request.</summary>
    private async Task<LyricsResult?> FetchAsync(MxmTrack t, string? knownSubtitle, string? knownPlain, CancellationToken cancellationToken)
    {
        var source = $"Musixmatch #{t.Id}";
        if (t.Instrumental) return new LyricsResult(new InstrumentalLyrics(source), t.Info);

        if (t.HasRichsync)
        {
            using var doc = await CallAsync("track.richsync.get", $"track_id={t.Id}", cancellationToken);
            if (Body(doc.RootElement) is { } body && TryGetPath(body, out var rs, "richsync")
                && GetString(rs, "richsync_body") is { Length: > 0 } richsync)
            {
                var lines = RichsyncToLines(richsync);
                if (lines.Count > 0) return new LyricsResult(new SyncedLyrics(lines, source), t.Info);
            }
        }

        var subtitle = knownSubtitle;
        if (subtitle is null && t.HasSubtitles)
        {
            using var doc = await CallAsync("track.subtitle.get", $"track_id={t.Id}&subtitle_format=lrc", cancellationToken);
            subtitle = Body(doc.RootElement) is { } body ? ReadSubtitle(body) : null;
        }
        if (LrcParser.IsLrc(subtitle))
        {
            var lines = LrcParser.Parse(subtitle!);
            if (lines.Count > 0) return new LyricsResult(new SyncedLyrics(lines, source), t.Info);
        }

        var plain = knownPlain;
        if (plain is null && t.HasLyrics)
        {
            using var doc = await CallAsync("track.lyrics.get", $"track_id={t.Id}", cancellationToken);
            plain = Body(doc.RootElement) is { } body ? ReadLyricsBody(body) : null;
        }
        return string.IsNullOrWhiteSpace(plain) ? null : new LyricsResult(new PlainLyrics(plain, source), t.Info);
    }

    internal static MxmTrack? ReadTrack(JsonElement track)
    {
        if (GetInt64(track, "track_id") is not { } id) return null;
        var info = new TrackInfo(
            GetString(track, "artist_name") ?? string.Empty,
            GetString(track, "track_name") ?? string.Empty,
            GetString(track, "album_name"),
            Seconds(GetInt64(track, "track_length")));
        return new MxmTrack(id, info, GetFlag(track, "instrumental"), GetFlag(track, "has_lyrics"),
            GetFlag(track, "has_subtitles"), GetFlag(track, "has_richsync"));
    }

    /// <summary>Picks from track.search's <c>track_list</c> ([{ "track": {...} }]).</summary>
    internal static MxmTrack? PickBest(TrackInfo query, JsonElement trackList)
    {
        if (trackList.ValueKind != JsonValueKind.Array) return null;

        MxmTrack? best = null;
        var bestScore = int.MinValue;
        foreach (var item in trackList.EnumerateArray())
        {
            if (!TryGetPath(item, out var el, "track") || ReadTrack(el) is not { } candidate) continue;

            var score = TrackMatcher.Score(query, candidate.Info);
            if (score < TrackMatcher.MinAcceptableScore) continue;

            // Prefer timed lyrics among equally good matches.
            if (candidate.HasRichsync || candidate.HasSubtitles) score += 1;
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// richsync_body is a JSON string: [{ "ts": start, "te": end, "x": "line text", "l": [{ "c": "word", "o": offset }] }].
    /// Word timings are dropped; each entry becomes one line at its start time.
    /// </summary>
    internal static IReadOnlyList<LyricLine> RichsyncToLines(string richsyncBody)
    {
        using var doc = JsonDocument.Parse(richsyncBody);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        var lines = new List<LyricLine>();
        TimeSpan? previousEnd = null;
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!TryGetSeconds(entry, "ts", out var start)) continue;

            var text = GetString(entry, "x");
            if (text is null && entry.TryGetProperty("l", out var words) && words.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var w in words.EnumerateArray()) sb.Append(GetString(w, "c"));
                text = sb.ToString();
            }

            if (previousEnd is { } end && start - end >= GapForBlankLine) lines.Add(new LyricLine(end, string.Empty));
            lines.Add(new LyricLine(start, (text ?? string.Empty).Trim()));
            previousEnd = TryGetSeconds(entry, "te", out var te) ? te : null;
        }
        return lines.OrderBy(l => l.Time).ToList();
    }

    /// <summary>track.subtitle.get has <c>subtitle</c>; the macro's track.subtitles.get has <c>subtitle_list[0].subtitle</c>.</summary>
    internal static string? ReadSubtitle(JsonElement body)
    {
        if (TryGetPath(body, out var single, "subtitle")) return GetString(single, "subtitle_body");
        if (TryGetPath(body, out var list, "subtitle_list") && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (TryGetPath(item, out var sub, "subtitle") && GetString(sub, "subtitle_body") is { Length: > 0 } text) return text;
            }
        }
        return null;
    }

    internal static string? ReadLyricsBody(JsonElement body)
    {
        if (!TryGetPath(body, out var lyrics, "lyrics") || GetString(lyrics, "lyrics_body") is not { } text) return null;

        // Restricted responses end with "******* This Lyrics is NOT for Commercial use *******" and a tracking id.
        var cut = text.IndexOf("\n*******", StringComparison.Ordinal);
        if (cut >= 0) text = text[..cut];
        text = NormalizePlainText(text);
        return text.Length == 0 ? null : text;
    }

    /// <summary>Body of one macro call, or null if it failed (404 calls have no body or an empty string).</summary>
    internal static JsonElement? MacroBody(JsonElement macroCalls, string name) =>
        TryGetPath(macroCalls, out var call, name) ? Body(call) : null;

    private static JsonElement? Body(JsonElement root) =>
        TryGetPath(root, out var header, "message", "header") && GetInt64(header, "status_code") == 200
        && TryGetPath(root, out var body, "message", "body") && body.ValueKind == JsonValueKind.Object
            ? body
            : null;

    private static bool TryGetSeconds(JsonElement entry, string name, out TimeSpan value)
    {
        value = default;
        if (!entry.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Number) return false;
        var seconds = el.GetDouble();
        if (seconds < 0 || double.IsNaN(seconds)) return false;
        value = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>Calls an API method with the user token, renewing it once when the server asks.</summary>
    private async Task<JsonDocument> CallAsync(string method, string query, CancellationToken cancellationToken)
    {
        var current = await GetTokenAsync(stale: null, cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            var doc = await GetJsonAsync($"{method}?app_id={AppId}&format=json&usertoken={Q(current)}&{query}", cancellationToken);
            var (status, hint) = ReadHeader(doc.RootElement);
            if (status != 401) return doc;

            doc.Dispose();
            if (attempt == 0 && hint is "renew" or "captcha")
            {
                current = await GetTokenAsync(current, cancellationToken);
                continue;
            }
            throw new HttpRequestException($"Musixmatch {method}: 401 ({hint ?? "yetkisiz"})");
        }
    }

    /// <summary>Returns the cached token, or fetches a new one if there is none or it equals <paramref name="stale"/>.</summary>
    private async Task<string> GetTokenAsync(string? stale, CancellationToken cancellationToken)
    {
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            token ??= ReadTokenFile();
            if (token is not null && token != stale) return token;

            if (DateTime.UtcNow < tokenBlockedUntil)
                throw new HttpRequestException("Musixmatch token alınamadı (bekleme süresi dolmadı)");

            using var doc = await GetJsonAsync($"token.get?app_id={AppId}&format=json", cancellationToken);
            var (status, hint) = ReadHeader(doc.RootElement);
            var newToken = TryGetPath(doc.RootElement, out var body, "message", "body") ? GetString(body, "user_token") : null;
            if (status != 200 || string.IsNullOrEmpty(newToken))
            {
                tokenBlockedUntil = DateTime.UtcNow + TokenCooldown;
                throw new HttpRequestException($"Musixmatch token alınamadı ({status}{(hint is null ? "" : ", " + hint)})");
            }
            WriteTokenFile(newToken);
            return token = newToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private string? ReadTokenFile()
    {
        try
        {
            return tokenFile is not null && File.Exists(tokenFile) && File.ReadAllText(tokenFile).Trim() is { Length: > 0 } t ? t : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteTokenFile(string value)
    {
        if (tokenFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tokenFile))!);
            File.WriteAllText(tokenFile, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the token just will not survive a restart.
        }
    }

    private static (long? Status, string? Hint) ReadHeader(JsonElement root) =>
        TryGetPath(root, out var header, "message", "header") ? (GetInt64(header, "status_code"), GetString(header, "hint")) : (null, null);

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
