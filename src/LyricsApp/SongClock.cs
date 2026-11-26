using System.Globalization;

namespace LyricsApp;

/// <summary>Times as a player shows them: 0:07, 3:45, 1:02:09.</summary>
public static class SongClock
{
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        var hours = (int)time.TotalHours;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes}:{time.Seconds:00}");
    }
}
