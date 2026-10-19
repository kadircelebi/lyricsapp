using System.Text.Json;
using LyricsApp.Core;
using LyricsApp.Core.Providers;

namespace LyricsApp.Core.Tests;

// All lyric text here is made up.
public class GeniusParsingTests
{
    [Fact]
    public void Extracts_containers_without_excluded_parts()
    {
        var html = """
        <html><body>
          <div class="header"><h1>Not lyrics</h1></div>
          <div data-lyrics-container="true" class="Lyrics__Container">
            <div data-exclude-from-selection="true"><button>12 Contributors</button><span>Translations</span></div>
            [Verse 1]<br>The <a href="/ann"><span>kettle</span></a> sings at <i>noon</i><br>
            Crumbs on the   windowsill<br><br><br><br><br>
            [Chorus]<br><b>Hum along</b>, hum along
          </div>
          <div class="ad">Buy now</div>
          <div data-lyrics-container="true"><div>Last stanza line</div>Fade out<br></div>
          <div data-lyrics-container="false">Should not appear</div>
        </body></html>
        """;

        var text = GeniusProvider.ExtractLyrics(html);

        Assert.Equal(
            "[Verse 1]\nThe kettle sings at noon\nCrumbs on the windowsill\n\n[Chorus]\nHum along, hum along\nLast stanza line\nFade out",
            text);
    }

    [Fact]
    public void Page_without_lyrics_container_gives_null()
    {
        Assert.Null(GeniusProvider.ExtractLyrics("<html><body><div>This song is an instrumental</div></body></html>"));
    }

    [Fact]
    public void Search_picks_song_hit_by_artist_and_ignores_translations_and_foreign_links()
    {
        using var doc = JsonDocument.Parse("""
        { "meta": { "status": 200 }, "response": { "hits": [
          { "type": "album", "result": { "title": "Song", "url": "https://genius.com/albums/x", "primary_artist": { "name": "Artist" } } },
          { "type": "song", "result": { "title": "Artist - Song (Çeviri)", "url": "https://genius.com/translations-1", "primary_artist": { "name": "Genius Çeviriler" } } },
          { "type": "song", "result": { "title": "Song", "url": "https://evil.example/Artist-song-lyrics", "primary_artist": { "name": "Artist" } } },
          { "type": "song", "result": { "title": "Song", "url": "https://genius.com/Other-artist-song-lyrics", "primary_artist": { "name": "Other" },
                                        "artist_names": "Other & Artist" } },
          { "type": "song", "result": { "title": "Song", "url": "https://genius.com/Artist-song-lyrics", "primary_artist": { "name": "Artist" } } }
        ] } }
        """);

        var best = GeniusProvider.PickBest(new TrackInfo("Artist", "Song"), doc.RootElement);

        Assert.Equal("https://genius.com/Artist-song-lyrics", best?.Url.ToString());
        Assert.False(best!.Instrumental);
    }

    [Fact]
    public void Search_uses_all_credited_artists_for_duets()
    {
        using var doc = JsonDocument.Parse("""
        { "response": { "hits": [
          { "type": "song", "result": { "title": "Duet", "url": "https://genius.com/a", "primary_artist": { "name": "Singer One" }, "artist_names": "Singer One & Singer Two" } }
        ] } }
        """);
        Assert.NotNull(GeniusProvider.PickBest(new TrackInfo("Singer Two", "Duet"), doc.RootElement));
        Assert.Null(GeniusProvider.PickBest(new TrackInfo("Singer Three", "Duet"), doc.RootElement));
    }
}
