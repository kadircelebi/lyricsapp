using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using static LyricsApp.Core.Providers.ProviderHelpers;

namespace LyricsApp.Core.Providers;

/// <summary>
/// genius.com: search through its web API, then scrape the song page. Plain lyrics only.
/// Unofficial; a page redesign can break the extraction.
/// </summary>
public sealed partial class GeniusProvider(HttpClient http) : ILyricsProvider
{
    public const string BaseAddress = "https://genius.com/";

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public string Name => "Genius";

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromSeconds(10) };
        // Genius serves the full page only to browser-like clients.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36");
        return client;
    }

    internal sealed record GeniusSong(Uri Url, TrackInfo Info, bool Instrumental);

    public async Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        if (track.IsEmpty) return null;

        GeniusSong? song;
        using (var response = await http.GetAsync($"api/search?q={Q($"{track.Artist} {track.Title}".Trim())}", cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            song = PickBest(track, doc.RootElement);
        }
        if (song is null) return null;

        var source = $"Genius: {song.Url.AbsolutePath.TrimStart('/')}";
        if (song.Instrumental) return new LyricsResult(new InstrumentalLyrics(source), song.Info);

        using var page = await http.GetAsync(song.Url, cancellationToken);
        if (page.StatusCode == HttpStatusCode.NotFound) return null;
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync(cancellationToken);

        var text = ExtractLyrics(html);
        return text is null ? null : new LyricsResult(new PlainLyrics(text, source), song.Info);
    }

    /// <summary>Picks from <c>response.hits[]</c> where <c>type == "song"</c>.</summary>
    internal static GeniusSong? PickBest(TrackInfo query, JsonElement root)
    {
        if (!TryGetPath(root, out var hits, "response", "hits") || hits.ValueKind != JsonValueKind.Array) return null;

        GeniusSong? best = null;
        var bestScore = int.MinValue;
        foreach (var hit in hits.EnumerateArray())
        {
            if (GetString(hit, "type") != "song" || !TryGetPath(hit, out var result, "result")) continue;

            // Only follow links that stay on genius.com.
            if (!Uri.TryCreate(GetString(result, "url"), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                || !(url.Host == "genius.com" || url.Host.EndsWith(".genius.com", StringComparison.OrdinalIgnoreCase)))
                continue;

            var title = GetString(result, "title") ?? string.Empty;
            var primary = TryGetPath(result, out var pa, "primary_artist") ? GetString(pa, "name") : null;
            var all = GetString(result, "artist_names");

            // "artist_names" lists every credited artist, which helps duets.
            var candidates = new[] { primary, all }.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => new TrackInfo(a!, title)).ToList();
            if (candidates.Count == 0) candidates.Add(new TrackInfo(string.Empty, title));
            var info = candidates.MaxBy(c => TrackMatcher.Score(query, c))!;

            var score = TrackMatcher.Score(query, info);
            if (score < TrackMatcher.MinAcceptableScore || score <= bestScore) continue;
            best = new GeniusSong(url, info, GetFlag(result, "instrumental"));
            bestScore = score;
        }
        return best;
    }

    /// <summary>
    /// Text of every <c>div[data-lyrics-container="true"]</c>, skipping <c>[data-exclude-from-selection]</c>
    /// (contributors, translations header). <c>&lt;br&gt;</c> and block boundaries become newlines.
    /// </summary>
    internal static string? ExtractLyrics(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var sb = new StringBuilder();
        foreach (var container in document.QuerySelectorAll("div[data-lyrics-container='true']"))
        {
            EnsureLineBreak(sb);
            AppendText(container, sb);
        }

        var text = NormalizePlainText(sb.ToString());
        return text.Length == 0 ? null : text;
    }

    // Block elements start a new line, but must not add a blank one after a <br>.
    private static void EnsureLineBreak(StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
    }

    private static void AppendText(INode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    sb.Append(WhitespaceRegex().Replace(text.Data, " "));
                    break;
                case IElement el when el.HasAttribute("data-exclude-from-selection"):
                    break;
                case IElement el when el.LocalName == "br":
                    sb.Append('\n');
                    break;
                case IElement el when el.LocalName is "script" or "style" or "button" or "svg":
                    break;
                case IElement el when el.LocalName is "div" or "p":
                    EnsureLineBreak(sb);
                    AppendText(el, sb);
                    EnsureLineBreak(sb);
                    break;
                case IElement el:
                    AppendText(el, sb);
                    break;
            }
        }
    }
}
