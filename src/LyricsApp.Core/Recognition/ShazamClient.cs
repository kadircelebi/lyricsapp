using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LyricsApp.Core.Providers;

namespace LyricsApp.Core.Recognition;

/// <summary>What Shazam said about one signature.</summary>
/// <param name="Track">The recognised song, or null when there is no match (yet).</param>
/// <param name="RetryAfter">How much audio Shazam wants before it tries again; null when it gave up.</param>
public sealed record ShazamMatch(TrackInfo? Track, TimeSpan? RetryAfter);

/// <summary>
/// Shazam's match endpoint, as used by its mobile apps. Unofficial: there is no public Shazam API for Windows,
/// so this may stop working at any time. Only the signature and a random id are sent, never audio.
/// </summary>
public sealed class ShazamClient(HttpClient http)
{
    public const string BaseAddress = "https://amp.shazam.com/";

    // Random per app run, so requests cannot be tied to one installation over time.
    private static readonly string InstallationId = Guid.NewGuid().ToString();

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LyricsApp/1.1");
        return client;
    }

    /// <param name="sessionId">The same id for every attempt on one song (Shazam's "tag").</param>
    public async Task<ShazamMatch> MatchAsync(byte[] signature, TimeSpan audioLength, string sessionId, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["signatures"] = new JsonArray(new JsonObject
            {
                ["uri"] = "data:audio/vnd.shazam.sig;base64," + Convert.ToBase64String(signature),
                ["samplems"] = (int)audioLength.TotalMilliseconds,
            }),
            ["timezone"] = "GMT",
        };

        using var response = await http.PostAsJsonAsync($"match/v1/en/US/android/{InstallationId}/{sessionId}", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return Parse(doc.RootElement);
    }

    internal static ShazamMatch Parse(JsonElement root)
    {
        if (ProviderHelpers.TryGetPath(root, out var matches, "results", "matches")
            && matches.ValueKind == JsonValueKind.Array && matches.GetArrayLength() > 0
            && ProviderHelpers.GetString(matches[0], "id") is { Length: > 0 } id
            && ProviderHelpers.TryGetPath(root, out var attributes, "resources", "shazam-songs", id, "attributes"))
        {
            var title = ProviderHelpers.GetString(attributes, "title");
            var artist = ProviderHelpers.GetString(attributes, "artist");
            if (!string.IsNullOrWhiteSpace(title))
                return new ShazamMatch(new TrackInfo(artist?.Trim() ?? string.Empty, title.Trim()), null);
        }

        var retry = ProviderHelpers.TryGetPath(root, out var retryElement, "results", "retry")
            ? ProviderHelpers.GetInt64(retryElement, "retryInMilliseconds")
            : null;
        return new ShazamMatch(null, retry is > 0 ? TimeSpan.FromMilliseconds(retry.Value) : null);
    }
}
