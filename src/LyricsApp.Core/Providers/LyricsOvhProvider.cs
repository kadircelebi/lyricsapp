using System.Net;
using System.Text.Json;
using static LyricsApp.Core.Providers.ProviderHelpers;

namespace LyricsApp.Core.Providers;

/// <summary>
/// https://lyrics.ovh — simple free API, plain lyrics only. Often slow or down.
/// Its suggest endpoint is a Deezer search, so candidates carry a duration.
/// </summary>
public sealed class LyricsOvhProvider(HttpClient http) : ILyricsProvider
{
    public const string BaseAddress = "https://api.lyrics.ovh/";

    public string Name => "lyrics.ovh";

    public static HttpClient CreateHttpClient() =>
        new() { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromSeconds(10) };

    public async Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        if (track.IsEmpty) return null;

        TrackInfo? match;
        using (var doc = await GetJsonAsync($"suggest/{Q($"{track.Artist} {track.Title}".Trim())}", cancellationToken))
        {
            match = doc is null ? null : PickBest(track, doc.RootElement);
        }
        if (match is null) return null;

        using var lyricsDoc = await GetJsonAsync($"v1/{Q(match.Artist)}/{Q(match.Title)}", cancellationToken);
        var text = lyricsDoc is null ? null : ReadLyrics(lyricsDoc.RootElement);
        return text is null ? null : new LyricsResult(new PlainLyrics(text, Name), match);
    }

    /// <summary>Picks from <c>data[]</c> (<c>title</c>, <c>artist.name</c>, <c>album.title</c>, <c>duration</c>).</summary>
    internal static TrackInfo? PickBest(TrackInfo query, JsonElement root)
    {
        if (!TryGetPath(root, out var data, "data") || data.ValueKind != JsonValueKind.Array) return null;

        TrackInfo? best = null;
        var bestScore = int.MinValue;
        foreach (var item in data.EnumerateArray())
        {
            var title = GetString(item, "title");
            var artist = TryGetPath(item, out var a, "artist") ? GetString(a, "name") : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) continue;

            var album = TryGetPath(item, out var al, "album") ? GetString(al, "title") : null;
            var candidate = new TrackInfo(artist, title, album, Seconds(GetInt64(item, "duration")));
            var score = TrackMatcher.Score(query, candidate);
            if (score < TrackMatcher.MinAcceptableScore || score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    internal static string? ReadLyrics(JsonElement root)
    {
        if (GetString(root, "lyrics") is not { } raw) return null;

        // Some answers start with a French header line: "Paroles de la chanson <title> par <artist>".
        var text = raw.Replace("\r\n", "\n");
        if (text.StartsWith("Paroles de la chanson ", StringComparison.OrdinalIgnoreCase))
        {
            var newline = text.IndexOf('\n');
            text = newline < 0 ? string.Empty : text[(newline + 1)..];
        }
        text = NormalizePlainText(text);
        return text.Length == 0 ? null : text;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
