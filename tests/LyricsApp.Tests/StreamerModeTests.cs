using System.Text.Json;
using System.Windows;

namespace LyricsApp.Tests;

public class StreamerModeTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1040);

    [Theory]
    // window left, top -> buttons at left, at top (window is 460 x 640)
    [InlineData(100, 50, false, false)]    // top-left of the screen: bottom-right corner of the window
    [InlineData(1400, 50, true, false)]    // top-right: bottom-left
    [InlineData(100, 380, false, true)]    // bottom-left: top-right
    [InlineData(1400, 380, true, true)]    // bottom-right: top-left
    public void Buttons_go_to_the_corner_nearest_the_middle_of_the_screen(double left, double top, bool atLeft, bool atTop) =>
        Assert.Equal((atLeft, atTop), StreamerControls.ChooseCorner(new Rect(left, top, 460, 640), Screen));

    [Fact]
    public void Corner_is_chosen_on_the_screen_the_window_is_on()
    {
        // A second screen to the left of the main one: its right half is still "right" there.
        var second = new Rect(-2560, 0, 2560, 1400);
        Assert.Equal((true, false), StreamerControls.ChooseCorner(new Rect(-700, 100, 460, 640), second));
        Assert.Equal((false, true), StreamerControls.ChooseCorner(new Rect(-2500, 700, 460, 640), second));
    }

    [Fact]
    public void Streamer_mode_is_off_by_default_and_is_saved_with_the_display_settings()
    {
        Assert.False(new DisplaySettings().StreamerMode);

        var saved = JsonSerializer.Serialize(new DisplaySettings { StreamerMode = true });
        Assert.True(JsonSerializer.Deserialize<DisplaySettings>(saved)!.StreamerMode);
        // Settings files written before the mode existed.
        Assert.False(JsonSerializer.Deserialize<DisplaySettings>("""{ "LyricsOnly": true }""")!.StreamerMode);
    }

    [Fact]
    public void The_song_progress_bar_is_on_by_default_and_can_be_switched_off()
    {
        Assert.True(new DisplaySettings().StreamerProgress);
        // Settings files written before the option existed.
        Assert.True(JsonSerializer.Deserialize<DisplaySettings>("""{ "StreamerMode": true }""")!.StreamerProgress);

        var saved = JsonSerializer.Serialize(new DisplaySettings { StreamerProgress = false });
        Assert.False(JsonSerializer.Deserialize<DisplaySettings>(saved)!.StreamerProgress);
    }

    [Fact]
    public void Switching_streamer_mode_is_announced_once()
    {
        var display = new DisplaySettings();
        var changes = new List<string?>();
        display.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        display.StreamerMode = true;
        display.StreamerMode = true;

        Assert.Equal([nameof(DisplaySettings.StreamerMode)], changes);
    }
}
