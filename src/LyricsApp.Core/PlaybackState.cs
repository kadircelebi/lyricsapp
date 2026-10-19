namespace LyricsApp.Core;

/// <summary>
/// A snapshot of the player's timeline. Players update the reported position rarely,
/// so the current position is extrapolated from the last report.
/// </summary>
public sealed record PlaybackState(bool IsPlaying, TimeSpan Position, DateTimeOffset UpdatedAt, double Rate = 1.0, TimeSpan? Duration = null)
{
    public static readonly PlaybackState Stopped = new(false, TimeSpan.Zero, DateTimeOffset.MinValue);

    public TimeSpan GetPosition(DateTimeOffset now)
    {
        if (!IsPlaying) return Position;

        var elapsed = now - UpdatedAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var position = Position + elapsed * (Rate > 0 ? Rate : 1.0);

        return Duration is { } d && d > TimeSpan.Zero && position > d ? d : position;
    }

    /// <summary>
    /// Some players report a missing or bogus LastUpdatedTime (e.g. year 1601).
    /// In that case trust the moment we received the report.
    /// </summary>
    public static DateTimeOffset SanitizeUpdatedAt(DateTimeOffset reported, DateTimeOffset receivedAt) =>
        reported > receivedAt + TimeSpan.FromSeconds(5) || reported < receivedAt - TimeSpan.FromHours(12) ? receivedAt : reported;
}
