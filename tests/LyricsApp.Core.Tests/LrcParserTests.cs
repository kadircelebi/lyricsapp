using LyricsApp.Core;

namespace LyricsApp.Core.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parses_timestamps_with_various_fraction_lengths()
    {
        var lines = LrcParser.Parse("[00:01.5]a\n[00:02.25]b\n[00:03.125]c\n[01:04]d");

        Assert.Equal(
            [TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(2250), TimeSpan.FromMilliseconds(3125), TimeSpan.FromSeconds(64)],
            lines.Select(l => l.Time));
        Assert.Equal(["a", "b", "c", "d"], lines.Select(l => l.Text));
    }

    [Fact]
    public void Expands_lines_with_multiple_timestamps_and_sorts()
    {
        var lines = LrcParser.Parse("[00:10.00][00:30.00]chorus\n[00:20.00]verse");

        Assert.Equal(["chorus", "verse", "chorus"], lines.Select(l => l.Text));
        Assert.Equal([10, 20, 30], lines.Select(l => (int)l.Time.TotalSeconds));
    }

    [Fact]
    public void Skips_metadata_and_applies_offset()
    {
        var lines = LrcParser.Parse("[ar:Someone]\n[ti:Something]\n[offset:+500]\n[00:02.00]line");

        var line = Assert.Single(lines);
        Assert.Equal(TimeSpan.FromSeconds(1.5), line.Time);
    }

    [Fact]
    public void Keeps_empty_instrumental_lines()
    {
        var lines = LrcParser.Parse("[00:01.00]one\n[00:05.00]\n[00:09.00]two");

        Assert.Equal(3, lines.Count);
        Assert.Equal(string.Empty, lines[1].Text);
    }

    [Theory]
    [InlineData("[00:12.34]hello", true)]
    [InlineData("just some text\nwithout timing", false)]
    [InlineData("", false)]
    public void Detects_lrc(string text, bool expected) => Assert.Equal(expected, LrcParser.IsLrc(text));

    [Fact]
    public void Format_round_trips()
    {
        var original = LrcParser.Parse("[00:01.50]a\n[02:03.40]b");
        var again = LrcParser.Parse(LrcParser.Format(original));
        Assert.Equal(original, again);
    }

    [Theory]
    [InlineData(0.5, -1)]
    [InlineData(1.0, 0)]
    [InlineData(4.99, 0)]
    [InlineData(5.0, 1)]
    [InlineData(100, 2)]
    public void Finds_active_line(double seconds, int expected)
    {
        var lines = LrcParser.Parse("[00:01.00]a\n[00:05.00]b\n[00:09.00]c");
        Assert.Equal(expected, LrcParser.FindActiveLine(lines, TimeSpan.FromSeconds(seconds)));
    }
}
