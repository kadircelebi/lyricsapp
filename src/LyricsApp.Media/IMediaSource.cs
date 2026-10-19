using LyricsApp.Core;

namespace LyricsApp.Media;

/// <summary>Where the "now playing" song comes from: the real Windows media session, or a demo for screenshots.</summary>
public interface IMediaSource : IDisposable
{
    /// <summary>Raised when the song changes (null = nothing playing). May fire on a background thread.</summary>
    event EventHandler<MediaTrack?>? TrackChanged;

    /// <summary>Raised when play/pause, position or rate changes. May fire on a background thread.</summary>
    event EventHandler<PlaybackState>? PlaybackChanged;

    MediaTrack? CurrentTrack { get; }

    PlaybackState Playback { get; }

    Task StartAsync();

    Task RefreshAsync();
}
