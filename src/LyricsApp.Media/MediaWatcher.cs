using LyricsApp.Core;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LyricsApp.Media;

/// <summary>The song the system media session reports, before any cleanup.</summary>
public sealed record MediaTrack(string SourceApp, TrackInfo Track, IRandomAccessStreamReference? Thumbnail);

/// <summary>
/// Listens to Windows' System Media Transport Controls (the media flyout data).
/// Event-driven: no polling loop. Events fire on background threads.
/// </summary>
public sealed class MediaWatcher : IMediaSource
{
    private static readonly TimeSpan PropertiesDebounce = TimeSpan.FromMilliseconds(400);

    private readonly object gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private CancellationTokenSource? propertiesCts;
    private bool disposed;

    public event EventHandler<MediaTrack?>? TrackChanged;
    public event EventHandler<PlaybackState>? PlaybackChanged;

    public MediaTrack? CurrentTrack { get; private set; }
    public PlaybackState Playback { get; private set; } = PlaybackState.Stopped;

    public async Task StartAsync()
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        manager.CurrentSessionChanged += OnCurrentSessionChanged;
        await AttachAsync(manager.GetCurrentSession());
    }

    /// <summary>Re-reads everything from the current session (e.g. a manual refresh).</summary>
    public Task RefreshAsync() => manager is null ? Task.CompletedTask : AttachAsync(manager.GetCurrentSession());

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            await AttachAsync(sender.GetCurrentSession());
        }
        catch (Exception)
        {
            // A session can vanish while we read it; the next event will bring us back in sync.
        }
    }

    private async Task AttachAsync(GlobalSystemMediaTransportControlsSession? newSession)
    {
        lock (gate)
        {
            if (disposed) return;
            if (session is not null)
            {
                session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            session = newSession;
            if (session is not null)
            {
                session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }

        if (newSession is null)
        {
            SetTrack(null);
            SetPlayback(PlaybackState.Stopped);
            return;
        }

        UpdatePlayback(newSession);
        await ReadPropertiesAsync(newSession, CancellationToken.None);
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        // Players often send several updates in a row (title first, then artist, then artwork).
        CancellationTokenSource cts;
        lock (gate)
        {
            propertiesCts?.Cancel();
            propertiesCts = cts = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PropertiesDebounce, cts.Token);
                await ReadPropertiesAsync(sender, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // Session went away mid-read.
            }
        });
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => UpdatePlayback(sender);

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => UpdatePlayback(sender);

    private async Task ReadPropertiesAsync(GlobalSystemMediaTransportControlsSession source, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var props = await source.TryGetMediaPropertiesAsync().AsTask(timeout.Token);
        if (!ReferenceEquals(source, session)) return;

        var timeline = source.GetTimelineProperties();
        var duration = timeline.EndTime - timeline.StartTime;
        var track = new TrackInfo(
            props.Artist ?? string.Empty,
            props.Title ?? string.Empty,
            string.IsNullOrWhiteSpace(props.AlbumTitle) ? null : props.AlbumTitle,
            duration > TimeSpan.Zero ? duration : null);

        SetTrack(track.IsEmpty ? null : new MediaTrack(source.SourceAppUserModelId, track, props.Thumbnail));
        UpdatePlayback(source);
    }

    private void UpdatePlayback(GlobalSystemMediaTransportControlsSession source)
    {
        try
        {
            var info = source.GetPlaybackInfo();
            var timeline = source.GetTimelineProperties();
            var now = DateTimeOffset.Now;
            var duration = timeline.EndTime - timeline.StartTime;
            SetPlayback(new PlaybackState(
                info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                timeline.Position - timeline.StartTime,
                PlaybackState.SanitizeUpdatedAt(timeline.LastUpdatedTime, now),
                info.PlaybackRate ?? 1.0,
                duration > TimeSpan.Zero ? duration : null));
        }
        catch (Exception)
        {
            // Session closed.
        }
    }

    private void SetTrack(MediaTrack? track)
    {
        // Only notify when the song itself changed, not when e.g. the artwork, album or duration arrived late.
        if (CurrentTrack?.Track.Artist == track?.Track.Artist && CurrentTrack?.Track.Title == track?.Track.Title
            && CurrentTrack?.SourceApp == track?.SourceApp)
        {
            CurrentTrack = track;
            return;
        }
        CurrentTrack = track;
        TrackChanged?.Invoke(this, track);
    }

    private void SetPlayback(PlaybackState state)
    {
        Playback = state;
        PlaybackChanged?.Invoke(this, state);
    }

    public static async Task<byte[]?> ReadThumbnailAsync(MediaTrack track)
    {
        if (track.Thumbnail is null) return null;
        try
        {
            using var stream = await track.Thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 10_000_000) return null;
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            propertiesCts?.Cancel();
            if (session is not null)
            {
                session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                session = null;
            }
            if (manager is not null) manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }
}
