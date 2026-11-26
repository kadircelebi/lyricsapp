using LyricsApp.Core;

namespace LyricsApp.Media;

/// <summary>A player that reports something right now, as the source menu shows it.</summary>
/// <param name="App">The application id; what <see cref="IMediaSource.PinnedApp"/> and the ignore list take.</param>
/// <param name="Name">The name to show.</param>
public sealed record MediaPlayerInfo(string App, string Name, bool IsFollowed);

/// <summary>Where the "now playing" song comes from: the real Windows media session, or a demo for screenshots.</summary>
public interface IMediaSource : IDisposable
{
    /// <summary>Raised when the song changes (null = nothing playing). May fire on a background thread.</summary>
    event EventHandler<MediaTrack?>? TrackChanged;

    /// <summary>Raised when play/pause, position or rate changes. May fire on a background thread.</summary>
    event EventHandler<PlaybackState>? PlaybackChanged;

    MediaTrack? CurrentTrack { get; }

    PlaybackState Playback { get; }

    /// <summary>The players that could be followed right now, one entry per app.</summary>
    IReadOnlyList<MediaPlayerInfo> Players { get; }

    /// <summary>
    /// The app chosen by hand; null leaves the choice to the automatic rule. While that app reports nothing the
    /// automatic rule decides, and the app is followed again as soon as it is back.
    /// </summary>
    string? PinnedApp { get; set; }

    /// <summary>Apps that are never followed, whatever they play.</summary>
    void SetIgnoredApps(IEnumerable<string> apps);

    /// <summary>The current song was searched and has no lyrics: another playing song may take its place.</summary>
    void ReportNoLyrics();

    Task StartAsync();

    Task RefreshAsync();
}
