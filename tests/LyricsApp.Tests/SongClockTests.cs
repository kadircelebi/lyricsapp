namespace LyricsApp.Tests;

public class SongClockTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(59.9, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(225, "3:45")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3729, "1:02:09")]
    [InlineData(90000, "25:00:00")]
    [InlineData(-5, "0:00")]
    public void Shows_time_the_way_a_player_does(double seconds, string expected) =>
        Assert.Equal(expected, SongClock.Format(TimeSpan.FromSeconds(seconds)));
}
