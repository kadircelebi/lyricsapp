using System.Text.Json;
using LyricsApp.Core;
using LyricsApp.Core.Providers;

namespace LyricsApp.Core.Tests;

// All lyric text here is made up.
public class LyricsOvhParsingTests
{
    [Fact]
    public void Suggest_picks_best_candidate_using_duration()
    {
        using var doc = JsonDocument.Parse("""
        { "data": [
          { "title": "Song", "duration": 600, "artist": { "name": "Artist" }, "album": { "title": "Live" } },
          { "title": "Song", "duration": 181, "artist": { "name": "Artist" }, "album": { "title": "Studio" } },
          { "title": "Song", "artist": { "name": "Cover Band" } },
          { "title": "Song" }
        ] }
        """);

        var best = LyricsOvhProvider.PickBest(new TrackInfo("Artist", "Song", null, TimeSpan.FromSeconds(180)), doc.RootElement);

        Assert.Equal("Studio", best?.Album);
    }

    [Fact]
    public void Suggest_without_data_gives_null()
    {
        using var doc = JsonDocument.Parse("""{ "error": "nothing" }""");
        Assert.Null(LyricsOvhProvider.PickBest(new TrackInfo("Artist", "Song"), doc.RootElement));
    }

    [Fact]
    public void Lyrics_are_cleaned_of_header_and_extra_blank_lines()
    {
        using var doc = JsonDocument.Parse("""{ "lyrics": "Paroles de la chanson Song par Artist\r\nPebbles in my pocket\n\n\n\nStreetlights blink  \n" }""");
        Assert.Equal("Pebbles in my pocket\n\nStreetlights blink", LyricsOvhProvider.ReadLyrics(doc.RootElement));

        using var empty = JsonDocument.Parse("""{ "lyrics": "  \n " }""");
        Assert.Null(LyricsOvhProvider.ReadLyrics(empty.RootElement));
    }
}
