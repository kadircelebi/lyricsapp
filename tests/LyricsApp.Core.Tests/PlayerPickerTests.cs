using LyricsApp.Core;

namespace LyricsApp.Core.Tests;

public class PlayerPickerTests
{
    private static readonly IReadOnlySet<string> NoneIgnored = new HashSet<string>();

    private static PlayerSnapshot Song(string app, bool playing, bool systemChoice = false, bool noLyrics = false) =>
        new(app, new TrackInfo("Artist", "Song", "Album", TimeSpan.FromMinutes(3)), playing, systemChoice, noLyrics);

    // What a browser reports for a long video: channel name as the artist, no album.
    private static PlayerSnapshot Talk(string app, bool playing, bool systemChoice = false) =>
        new(app, new TrackInfo("Channel", "An hour of talking", null, TimeSpan.FromMinutes(63)), playing, systemChoice);

    private static int Pick(IReadOnlyList<PlayerSnapshot> players, int followed, string? pinned = null, params string[] ignored) =>
        PlayerPicker.Pick(players, followed, pinned, ignored.Length == 0 ? NoneIgnored : ignored.ToHashSet());

    [Fact]
    public void Nothing_to_follow_without_players() => Assert.Equal(-1, Pick([], -1));

    [Fact]
    public void A_single_player_is_followed_whatever_it_plays()
    {
        Assert.Equal(0, Pick([Song("music", playing: false)], -1));
        Assert.Equal(0, Pick([Talk("browser", playing: true)], -1));
    }

    [Fact]
    public void A_video_started_next_to_the_music_does_not_take_over()
    {
        // Windows now calls the browser current; the song keeps playing.
        var players = new[] { Song("music", playing: true), Talk("browser", playing: true, systemChoice: true) };
        Assert.Equal(0, Pick(players, followed: 0));
    }

    [Fact]
    public void A_second_song_does_not_take_over_while_the_first_still_plays()
    {
        var players = new[] { Song("music", playing: true), Song("browser", playing: true, systemChoice: true) };
        Assert.Equal(0, Pick(players, followed: 0));
    }

    [Fact]
    public void The_music_moves_to_the_player_that_plays_when_the_followed_one_pauses()
    {
        var players = new[] { Song("music", playing: false), Song("browser", playing: true) };
        Assert.Equal(1, Pick(players, followed: 0));
    }

    [Fact]
    public void A_paused_song_stays_while_only_a_long_programme_plays()
    {
        var players = new[] { Song("music", playing: false), Talk("browser", playing: true, systemChoice: true) };
        Assert.Equal(0, Pick(players, followed: 0));
        // Also when the app starts in that situation and follows nothing yet.
        Assert.Equal(0, Pick(players, followed: -1));
    }

    [Fact]
    public void A_song_takes_over_from_a_long_programme()
    {
        var players = new[] { Talk("browser", playing: true, systemChoice: true), Song("music", playing: true) };
        Assert.Equal(1, Pick(players, followed: 0));
    }

    [Fact]
    public void Windows_choice_decides_between_equals_when_nothing_is_followed()
    {
        var players = new[] { Song("a", playing: true), Song("b", playing: true, systemChoice: true) };
        Assert.Equal(1, Pick(players, followed: -1));
        Assert.Equal(0, Pick([Song("a", playing: true), Song("b", playing: true)], followed: -1));
    }

    [Fact]
    public void A_playing_song_without_lyrics_gives_way_to_another_playing_song()
    {
        // A short video was taken for a song; the music behind it has lyrics.
        var players = new[] { Song("browser", playing: true, noLyrics: true), Song("music", playing: true) };
        Assert.Equal(1, Pick(players, followed: 0));
    }

    [Fact]
    public void Two_songs_without_lyrics_do_not_swap_back_and_forth()
    {
        var players = new[] { Song("a", playing: true, noLyrics: true), Song("b", playing: true, noLyrics: true) };
        Assert.Equal(1, Pick(players, followed: 1));
        Assert.Equal(0, Pick(players, followed: 0));
    }

    [Fact]
    public void A_playing_song_without_lyrics_still_beats_a_paused_one()
    {
        var players = new[] { Song("music", playing: true, noLyrics: true), Song("browser", playing: false) };
        Assert.Equal(0, Pick(players, followed: 0));
    }

    [Fact]
    public void Ignored_apps_are_never_followed()
    {
        var players = new[] { Song("browser", playing: true, systemChoice: true), Song("music", playing: false) };
        Assert.Equal(1, Pick(players, followed: 0, pinned: null, "browser"));
        Assert.Equal(-1, Pick([Song("browser", playing: true)], followed: 0, pinned: null, "browser"));
    }

    [Fact]
    public void A_player_without_a_title_is_not_followed()
    {
        var empty = new PlayerSnapshot("browser", new TrackInfo(string.Empty, string.Empty), IsPlaying: true, IsSystemChoice: true);
        Assert.Equal(-1, Pick([empty], followed: -1));
        Assert.Equal(1, Pick([empty, Song("music", playing: false)], followed: -1));
    }

    [Fact]
    public void The_hand_picked_app_wins_even_paused_or_playing_a_long_programme()
    {
        var players = new[] { Song("music", playing: true), Talk("browser", playing: false) };
        Assert.Equal(1, Pick(players, followed: 0, pinned: "browser"));
    }

    [Fact]
    public void A_hand_picked_app_that_is_gone_leaves_the_choice_to_the_rule()
    {
        var players = new[] { Song("music", playing: true), Talk("browser", playing: true) };
        Assert.Equal(0, Pick(players, followed: -1, pinned: "closed player"));
    }

    [Fact]
    public void Of_two_players_of_the_hand_picked_app_the_playing_one_is_meant()
    {
        var players = new[] { Song("browser", playing: false), Song("browser", playing: true), Song("music", playing: true) };
        Assert.Equal(1, Pick(players, followed: 2, pinned: "browser"));
    }

    [Theory]
    // minutes, album, is a song
    [InlineData(3.5, null, true)]
    [InlineData(14.9, null, true)]
    [InlineData(15.0, null, false)]    // a talk, a mix, a film
    [InlineData(63.0, null, false)]
    [InlineData(23.0, "Album", true)]  // a long track from an album is still a song
    [InlineData(63.0, " ", false)]
    public void Long_recordings_without_an_album_are_not_songs(double minutes, string? album, bool expected) =>
        Assert.Equal(expected, PlayerPicker.IsSong(new TrackInfo("Artist", "Title", album, TimeSpan.FromMinutes(minutes))));

    [Fact]
    public void Unknown_length_counts_as_a_song() =>
        Assert.True(PlayerPicker.IsSong(new TrackInfo("Station", "Live radio")));
}
