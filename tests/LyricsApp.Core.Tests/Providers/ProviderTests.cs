using System.Text.Json;
using LyricsApp.Core;
using LyricsApp.Core.Providers;

namespace LyricsApp.Core.Tests;

public class ProviderTests
{
    private static readonly TrackInfo Query = new("Artist", "Song", null, TimeSpan.FromSeconds(180));

    [Fact]
    public void LrcLib_picks_acceptable_synced_result()
    {
        using var doc = JsonDocument.Parse("""
        [
          { "id": 1, "trackName": "Other Song", "artistName": "Artist", "duration": 180, "syncedLyrics": "[00:01.00]x" },
          { "id": 2, "trackName": "Song", "artistName": "Artist", "duration": 181, "plainLyrics": "plain" },
          { "id": 3, "trackName": "Song", "artistName": "Artist", "duration": 180, "syncedLyrics": "[00:01.00]one\n[00:02.00]two" }
        ]
        """);

        var best = LrcLibProvider.PickBest(Query, doc.RootElement);

        var synced = Assert.IsType<SyncedLyrics>(best?.Lyrics);
        Assert.Equal("LrcLib #3", synced.Source);
        Assert.Equal(2, synced.Lines.Count);
    }

    [Fact]
    public void LrcLib_returns_null_when_nothing_matches()
    {
        using var doc = JsonDocument.Parse("""[ { "id": 9, "trackName": "Else", "artistName": "Nobody", "syncedLyrics": "[00:01.00]x" } ]""");
        Assert.Null(LrcLibProvider.PickBest(Query, doc.RootElement));
    }

    [Fact]
    public void LrcLib_maps_instrumental()
    {
        using var doc = JsonDocument.Parse("""{ "id": 5, "trackName": "Song", "artistName": "Artist", "instrumental": true }""");
        Assert.IsType<InstrumentalLyrics>(LrcLibProvider.ToResult(doc.RootElement)?.Lyrics);
    }

    [Fact]
    public async Task Local_folder_saves_and_finds_lrc_case_insensitively()
    {
        var dir = Directory.CreateTempSubdirectory("lyricsapp-test-");
        try
        {
            var provider = new LocalFolderProvider(dir.FullName);
            var lyrics = new SyncedLyrics(LrcParser.Parse("[00:01.00]one\n[00:03.00]two"), "test");

            var path = await provider.SaveAsync(new TrackInfo("Art/ist", "Song?"), lyrics, CancellationToken.None);
            Assert.Equal("Art_ist - Song_.lrc", Path.GetFileName(path));

            var found = await provider.FindAsync(new TrackInfo("ART/IST", "song?"), CancellationToken.None);
            Assert.Equal(lyrics.Lines, Assert.IsType<SyncedLyrics>(found?.Lyrics).Lines);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Service_prefers_later_synced_over_earlier_plain_and_caches_it()
    {
        var dir = Directory.CreateTempSubdirectory("lyricsapp-test-");
        try
        {
            var cache = new LocalFolderProvider(dir.FullName);
            var plain = new FakeProvider("A", new PlainLyrics("text", "A"));
            var failing = new FakeProvider("B", error: new HttpRequestException("offline"));
            var synced = new FakeProvider("C", new SyncedLyrics(LrcParser.Parse("[00:01.00]x"), "C"));
            var service = new LyricsService([cache, plain, failing, synced], cache);

            var result = await service.FindAsync(Query, CancellationToken.None);

            Assert.Equal("C", result.ProviderName);
            Assert.IsType<SyncedLyrics>(result.Result?.Lyrics);
            Assert.Single(result.Errors);
            Assert.True(File.Exists(Path.Combine(dir.FullName, "Artist - Song.lrc")));

            // Second lookup is served from the cache without asking the others.
            var again = await new LyricsService([cache, new FakeProvider("X", error: new InvalidOperationException("must not be called"))], cache)
                .FindAsync(Query, CancellationToken.None);
            Assert.Equal(cache.Name, again.ProviderName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Service_falls_back_to_plain_when_no_synced_exists()
    {
        var service = new LyricsService([new FakeProvider("A", null), new FakeProvider("B", new PlainLyrics("text", "B"))], cache: null);
        var result = await service.FindAsync(Query, CancellationToken.None);
        Assert.Equal("B", result.ProviderName);
    }

    private sealed class FakeProvider(string name, Lyrics? lyrics = null, Exception? error = null) : ILyricsProvider
    {
        public string Name => name;

        public Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken) =>
            error is not null
                ? Task.FromException<LyricsResult?>(error)
                : Task.FromResult(lyrics is null ? null : new LyricsResult(lyrics, track));
    }
}
