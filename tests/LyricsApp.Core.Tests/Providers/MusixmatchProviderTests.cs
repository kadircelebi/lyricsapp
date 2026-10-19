using System.Text.Json;
using LyricsApp.Core;
using LyricsApp.Core.Providers;

namespace LyricsApp.Core.Tests;

// All lyric text here is made up.
public class MusixmatchParsingTests
{
    private static readonly TrackInfo Query = new("Artist", "Song", null, TimeSpan.FromSeconds(200));

    [Fact]
    public void Richsync_becomes_one_line_per_entry_with_blank_lines_in_long_gaps()
    {
        var body = """
        [
          { "ts": 1.5, "te": 4.0, "x": "Paper boats on a cold river", "l": [{ "c": "Paper", "o": 0 }, { "c": " ", "o": 0.4 }] },
          { "ts": 4.2, "te": 7.9, "l": [{ "c": "Lanterns", "o": 0 }, { "c": " ", "o": 0.5 }, { "c": "hum", "o": 0.6 }] },
          { "ts": 20.25, "te": 23.0, "x": "  Morning finds the harbour  " }
        ]
        """;

        var lines = MusixmatchProvider.RichsyncToLines(body);

        Assert.Equal(
        [
            new LyricLine(TimeSpan.FromSeconds(1.5), "Paper boats on a cold river"),
            new LyricLine(TimeSpan.FromSeconds(4.2), "Lanterns hum"),
            new LyricLine(TimeSpan.FromSeconds(7.9), string.Empty),
            new LyricLine(TimeSpan.FromSeconds(20.25), "Morning finds the harbour"),
        ], lines);
        Assert.StartsWith("[00:01.50]Paper boats", LrcParser.Format(lines));
    }

    [Fact]
    public void Richsync_that_is_not_an_array_gives_no_lines()
    {
        Assert.Empty(MusixmatchProvider.RichsyncToLines("""{ "ts": 1 }"""));
    }

    [Fact]
    public void Search_picks_best_match_and_prefers_timed_lyrics()
    {
        using var doc = JsonDocument.Parse("""
        [
          { "track": { "track_id": 1, "track_name": "Another Song", "artist_name": "Artist", "track_length": 200, "has_subtitles": 1 } },
          { "track": { "track_id": 2, "track_name": "Song", "artist_name": "Artist", "track_length": 200, "has_lyrics": 1 } },
          { "track": { "track_id": 3, "track_name": "Song", "artist_name": "Artist", "track_length": 201, "has_lyrics": 1, "has_richsync": 1 } },
          { "track": { "track_id": 4, "track_name": "Song", "artist_name": "Artist", "track_length": 400, "has_richsync": 1 } }
        ]
        """);

        var best = MusixmatchProvider.PickBest(Query, doc.RootElement);

        Assert.Equal(3, best?.Id);
        Assert.True(best!.HasRichsync);
        Assert.Equal(TimeSpan.FromSeconds(201), best.Info.Duration);
    }

    [Fact]
    public void Search_returns_null_for_non_matching_or_malformed_lists()
    {
        using var doc = JsonDocument.Parse("""[ { "track": { "track_id": 9, "track_name": "Else", "artist_name": "Nobody" } }, { "nope": 1 } ]""");
        Assert.Null(MusixmatchProvider.PickBest(Query, doc.RootElement));

        using var empty = JsonDocument.Parse("\"\"");
        Assert.Null(MusixmatchProvider.PickBest(Query, empty.RootElement));
    }

    [Fact]
    public void Macro_bodies_are_read_and_failed_calls_are_skipped()
    {
        using var doc = JsonDocument.Parse("""
        {
          "matcher.track.get": { "message": { "header": { "status_code": 404 }, "body": "" } },
          "track.subtitles.get": { "message": { "header": { "status_code": 200 }, "body": {
            "subtitle_list": [ { "subtitle": { "subtitle_body": "[00:02.00]Tin roof rain" } } ] } } },
          "track.lyrics.get": { "message": { "header": { "status_code": 200 }, "body": {
            "lyrics": { "lyrics_body": "Tin roof rain\r\nKettle song\n\n\n\nGate creaks\n...\n\n******* This Lyrics is NOT for Commercial use *******\n(123)" } } } }
        }
        """);
        var calls = doc.RootElement;

        Assert.Null(MusixmatchProvider.MacroBody(calls, "matcher.track.get"));
        Assert.Null(MusixmatchProvider.MacroBody(calls, "missing.call"));
        Assert.Equal("[00:02.00]Tin roof rain", MusixmatchProvider.ReadSubtitle(MusixmatchProvider.MacroBody(calls, "track.subtitles.get")!.Value));
        Assert.Equal("Tin roof rain\nKettle song\n\nGate creaks\n...", MusixmatchProvider.ReadLyricsBody(MusixmatchProvider.MacroBody(calls, "track.lyrics.get")!.Value));
    }

    [Fact]
    public async Task Renews_a_stale_saved_token_once_and_saves_the_new_one()
    {
        var dir = Directory.CreateTempSubdirectory("lyricsapp-test-");
        try
        {
            var tokenFile = Path.Combine(dir.FullName, "token.txt");
            await File.WriteAllTextAsync(tokenFile, "old");
            var tokenRequests = 0;
            using var http = new HttpClient(new StubHandler(uri =>
            {
                var url = uri.ToString();
                if (url.Contains("token.get"))
                {
                    tokenRequests++;
                    return """{ "message": { "header": { "status_code": 200 }, "body": { "user_token": "new" } } }""";
                }
                if (url.Contains("usertoken=old")) return """{ "message": { "header": { "status_code": 401, "hint": "renew" } } }""";
                if (url.Contains("macro.subtitles.get"))
                    return """{ "message": { "header": { "status_code": 200 }, "body": { "macro_calls": { "matcher.track.get": { "message": { "header": { "status_code": 404 }, "body": "" } } } } } }""";
                return """{ "message": { "header": { "status_code": 200 }, "body": { "track_list": [] } } }""";
            })) { BaseAddress = new Uri(MusixmatchProvider.BaseAddress) };

            var provider = new MusixmatchProvider(http, tokenFile);
            Assert.Null(await provider.FindAsync(Query, CancellationToken.None));

            Assert.Equal(1, tokenRequests);
            Assert.Equal("new", await File.ReadAllTextAsync(tokenFile));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class StubHandler(Func<Uri, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(respond(request.RequestUri!)) });
    }

    [Fact]
    public void Track_flags_are_read()
    {
        using var doc = JsonDocument.Parse("""{ "track_id": 7, "track_name": "Song", "artist_name": "Artist", "album_name": "LP", "instrumental": 1, "has_subtitles": 0 }""");
        var t = MusixmatchProvider.ReadTrack(doc.RootElement);
        Assert.NotNull(t);
        Assert.True(t.Instrumental);
        Assert.False(t.HasSubtitles);
        Assert.Equal("LP", t.Info.Album);
    }
}
