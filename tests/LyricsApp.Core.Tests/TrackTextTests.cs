using LyricsApp.Core;

namespace LyricsApp.Core.Tests;

public class TrackTextTests
{
    [Theory]
    [InlineData("Song Name (Official Music Video)", "Song Name")]
    [InlineData("Song Name [Lyrics]", "Song Name")]
    [InlineData("Song Name (4K Remastered)", "Song Name")]
    [InlineData("Song Name | Official Audio", "Song Name")]
    [InlineData("Şarkı Adı (Resmi Klip)", "Şarkı Adı")]
    [InlineData("Song (feat. Other)", "Song (feat. Other)")]
    [InlineData("Song (Live)", "Song (Live)")]
    public void Cleans_title_noise(string input, string expected) => Assert.Equal(expected, TrackTextCleaner.CleanTitle(input));

    [Theory]
    [InlineData("Artist - Topic", "Artist")]
    [InlineData("SomeArtistVEVO", "Some Artist")]
    [InlineData("Two Words VEVO", "Two Words")]
    [InlineData("​Artist", "Artist")]
    public void Cleans_artist(string input, string expected) => Assert.Equal(expected, TrackTextCleaner.CleanArtist(input));

    [Fact]
    public void Splits_artist_out_of_video_title()
    {
        var cleaned = TrackTextCleaner.Clean(new TrackInfo("SemicenkVEVO", "Semicenk & Büken - Eyvah Neye Yarar (Official Video)"));

        Assert.Equal("Semicenk & Büken", cleaned.Artist);
        Assert.Equal("Eyvah Neye Yarar", cleaned.Title);
    }

    [Fact]
    public void Splits_when_artist_is_empty()
    {
        var cleaned = TrackTextCleaner.Clean(new TrackInfo("", "Artist - Song"));
        Assert.Equal(("Artist", "Song"), (cleaned.Artist, cleaned.Title));
    }

    [Fact]
    public void Keeps_dash_titles_from_unrelated_artist()
    {
        var cleaned = TrackTextCleaner.Clean(new TrackInfo("Real Artist", "Part One - Reprise"));
        Assert.Equal(("Real Artist", "Part One - Reprise"), (cleaned.Artist, cleaned.Title));
    }

    [Theory]
    [InlineData("Şarkı Adı", "sarki adi")]
    [InlineData("The Band (Remix) feat. X", "band")]
    [InlineData("Ç-ğ, ö! ü?", "c g o u")]
    public void Normalizes(string input, string expected) => Assert.Equal(expected, TrackMatcher.Normalize(input));

    [Fact]
    public void Accepts_same_song_with_accent_and_artist_differences()
    {
        var query = new TrackInfo("Semicenk ve Büken", "Eyvah Neye Yarar", null, TimeSpan.FromSeconds(200));
        var candidate = new TrackInfo("Semicenk & Buken", "Eyvah Neye Yarar", null, TimeSpan.FromSeconds(201));
        Assert.True(TrackMatcher.IsAcceptable(query, candidate));
    }

    [Fact]
    public void Rejects_same_title_by_different_artist()
    {
        var query = new TrackInfo("Artist One", "Hello");
        Assert.False(TrackMatcher.IsAcceptable(query, new TrackInfo("Someone Else", "Hello")));
    }

    [Fact]
    public void Rejects_when_duration_is_far_off()
    {
        var query = new TrackInfo("Artist", "Song", null, TimeSpan.FromSeconds(200));
        Assert.False(TrackMatcher.IsAcceptable(query, new TrackInfo("Artist", "Song (Extended)", null, TimeSpan.FromSeconds(420))));
    }

    [Fact]
    public void Partial_match_needs_whole_words()
    {
        // Seen live: "Malika - Anne" matched "Malik - Les années 30" because of substring matching.
        Assert.False(TrackMatcher.IsAcceptable(new TrackInfo("Malika", "Anne"), new TrackInfo("Malik", "Les années 30")));
        Assert.True(TrackMatcher.IsAcceptable(new TrackInfo("Artist", "Anne"), new TrackInfo("Artist", "Anne (Akustik)")));
        Assert.True(TrackMatcher.IsAcceptable(new TrackInfo("Artist", "Anne"), new TrackInfo("Artist", "Anne Remastered")));
    }

    [Fact]
    public void Prefers_exact_match_over_partial()
    {
        var query = new TrackInfo("Artist", "Song");
        Assert.True(TrackMatcher.Score(query, new TrackInfo("Artist", "Song")) > TrackMatcher.Score(query, new TrackInfo("Artist", "Song Remix Version")));
    }
}
