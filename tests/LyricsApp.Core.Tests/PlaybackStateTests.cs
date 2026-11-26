namespace LyricsApp.Core.Tests;

public class PlaybackStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Extrapolates_while_playing()
    {
        var state = new PlaybackState(true, TimeSpan.FromSeconds(10), T0);
        Assert.Equal(TimeSpan.FromSeconds(12.5), state.GetPosition(T0.AddSeconds(2.5)));
    }

    [Fact]
    public void Holds_position_when_paused_and_clamps_to_duration()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), new PlaybackState(false, TimeSpan.FromSeconds(10), T0).GetPosition(T0.AddMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(60), new PlaybackState(true, TimeSpan.FromSeconds(50), T0, 1, TimeSpan.FromSeconds(60)).GetPosition(T0.AddMinutes(5)));
    }

    [Fact]
    public void Progress_runs_from_zero_to_one_over_the_song()
    {
        var playing = new PlaybackState(true, TimeSpan.FromSeconds(30), T0, 1, TimeSpan.FromSeconds(200));
        Assert.Equal(0.15, playing.GetProgress(T0)!.Value, 6);
        Assert.Equal(0.5, playing.GetProgress(T0.AddSeconds(70))!.Value, 6);
        Assert.Equal(1.0, playing.GetProgress(T0.AddMinutes(10)));

        var paused = playing with { IsPlaying = false };
        Assert.Equal(0.15, paused.GetProgress(T0.AddMinutes(10))!.Value, 6);
    }

    [Fact]
    public void No_progress_without_a_song_length()
    {
        Assert.Null(new PlaybackState(true, TimeSpan.FromSeconds(30), T0).GetProgress(T0));
        Assert.Null(new PlaybackState(true, TimeSpan.FromSeconds(30), T0, 1, TimeSpan.Zero).GetProgress(T0));
        Assert.Null(PlaybackState.Stopped.GetProgress(T0));
    }

    [Fact]
    public void Replaces_bogus_update_time()
    {
        var received = T0;
        Assert.Equal(received, PlaybackState.SanitizeUpdatedAt(new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero), received));
        Assert.Equal(T0.AddSeconds(-3), PlaybackState.SanitizeUpdatedAt(T0.AddSeconds(-3), received));
    }
}
