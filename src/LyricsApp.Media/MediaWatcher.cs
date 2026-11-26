using LyricsApp.Core;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LyricsApp.Media;

/// <summary>The song the system media session reports, before any cleanup.</summary>
public sealed record MediaTrack(string SourceApp, TrackInfo Track, IRandomAccessStreamReference? Thumbnail);

/// <summary>
/// Listens to Windows' System Media Transport Controls (the media flyout data).
/// Event-driven: no polling loop. Events fire on background threads.
/// Watches every player and follows the one <see cref="PlayerPicker"/> chooses, rather than the one Windows calls
/// current: that one changes to whatever started last, such as a video opened next to the music.
/// </summary>
public sealed class MediaWatcher : IMediaSource
{
    private static readonly TimeSpan PropertiesDebounce = TimeSpan.FromMilliseconds(400);
    private const int RememberedSongsWithoutLyrics = 200;

    /// <summary>
    /// One player, kept across changes of the system's list: Windows hands out new session objects whenever any
    /// player opens or closes, and those are not equal to the old ones, so a player is known by its app id (and its
    /// place among the players of that app).
    /// </summary>
    private sealed class Player(string app)
    {
        public string App { get; } = app;
        public GlobalSystemMediaTransportControlsSession? Session { get; set; }
        public TrackInfo Track { get; set; } = new(string.Empty, string.Empty);
        public IRandomAccessStreamReference? Thumbnail { get; set; }
        public CancellationTokenSource? PropertiesCts { get; set; }
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? OnProperties { get; set; }
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? OnPlayback { get; set; }
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>? OnTimeline { get; set; }
    }

    // Guards everything below. The events are raised while it is held, so that two changes arriving together cannot
    // be reported out of order; handlers must not block (the view model only posts to its dispatcher).
    private readonly object gate = new();
    private readonly List<Player> players = [];
    private readonly HashSet<string> ignoredApps = new(StringComparer.Ordinal);
    private readonly HashSet<string> songsWithoutLyrics = new(StringComparer.Ordinal);
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private Player? followed;
    private string? pinnedApp;
    private bool disposed;

    public event EventHandler<MediaTrack?>? TrackChanged;
    public event EventHandler<PlaybackState>? PlaybackChanged;

    public MediaTrack? CurrentTrack { get; private set; }
    public PlaybackState Playback { get; private set; } = PlaybackState.Stopped;

    public IReadOnlyList<MediaPlayerInfo> Players
    {
        get
        {
            lock (gate)
            {
                return players
                    .Where(p => !p.Track.IsEmpty && !ignoredApps.Contains(p.App))
                    .GroupBy(p => p.App)
                    .Select(group => new MediaPlayerInfo(group.Key, AppNames.Get(group.Key), group.Any(p => ReferenceEquals(p, followed))))
                    .ToList();
            }
        }
    }

    public string? PinnedApp
    {
        get
        {
            lock (gate) return pinnedApp;
        }
        set
        {
            lock (gate) pinnedApp = value;
            Choose();
        }
    }

    public void SetIgnoredApps(IEnumerable<string> apps)
    {
        lock (gate)
        {
            ignoredApps.Clear();
            ignoredApps.UnionWith(apps);
            if (pinnedApp is not null && ignoredApps.Contains(pinnedApp)) pinnedApp = null;
        }
        Choose();
    }

    public void ReportNoLyrics()
    {
        lock (gate)
        {
            if (followed is null) return;
            if (songsWithoutLyrics.Count >= RememberedSongsWithoutLyrics) songsWithoutLyrics.Clear();
            songsWithoutLyrics.Add(SongKey(followed));
        }
        Choose();
    }

    public async Task SendAsync(MediaCommand command)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (gate) session = followed?.Session;
        if (session is null) return;
        try
        {
            _ = command switch
            {
                MediaCommand.Previous => await session.TrySkipPreviousAsync(),
                MediaCommand.Next => await session.TrySkipNextAsync(),
                _ => await session.TryTogglePlayPauseAsync(),
            };
        }
        catch (Exception)
        {
            // The player closed, or does not take this command.
        }
    }

    public async Task StartAsync()
    {
        var created = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        lock (gate)
        {
            if (disposed) return;
            manager = created;
            created.SessionsChanged += OnSessionsChanged;
            created.CurrentSessionChanged += OnCurrentSessionChanged;
        }
        await SyncPlayersAsync();
    }

    /// <summary>Re-reads everything from every player (e.g. a manual refresh).</summary>
    public Task RefreshAsync() => SyncPlayersAsync();

    private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        try
        {
            await SyncPlayersAsync();
        }
        catch (Exception)
        {
            // A session can vanish while we read it; the next event will bring us back in sync.
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Choose();

    /// <summary>Brings the player list in line with what the system reports now, then reads every player again.</summary>
    private async Task SyncPlayersAsync()
    {
        List<Player> current;
        lock (gate)
        {
            if (disposed || manager is null) return;
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
            try
            {
                sessions = manager.GetSessions();
            }
            catch (Exception)
            {
                return;
            }

            // A player we already know keeps its song while its session object is swapped for the new one;
            // otherwise every open or close of any player would look like "nothing is playing" for a moment.
            var known = players.ToList();
            players.Clear();
            foreach (var session in sessions)
            {
                try
                {
                    var app = session.SourceAppUserModelId ?? string.Empty;
                    var player = known.FirstOrDefault(p => p.App == app) ?? new Player(app);
                    known.Remove(player);
                    Attach(player, session);
                    players.Add(player);
                }
                catch (Exception)
                {
                    // Closed between the listing and now.
                }
            }
            foreach (var gone in known)
            {
                Detach(gone);
                if (ReferenceEquals(followed, gone)) followed = null;
            }

            current = [.. players];
        }

        foreach (var player in current)
        {
            try
            {
                await ReadPropertiesAsync(player, CancellationToken.None);
            }
            catch (Exception)
            {
                // Closed mid-read; it keeps what it reported before.
            }
        }
        Choose();
    }

    private void Attach(Player player, GlobalSystemMediaTransportControlsSession session)
    {
        if (ReferenceEquals(player.Session, session)) return;
        Detach(player);
        player.Session = session;
        player.OnProperties = (_, _) => OnMediaPropertiesChanged(player);
        player.OnPlayback = (_, _) => Choose();
        player.OnTimeline = (_, _) => OnTimelinePropertiesChanged(player);
        session.MediaPropertiesChanged += player.OnProperties;
        session.PlaybackInfoChanged += player.OnPlayback;
        session.TimelinePropertiesChanged += player.OnTimeline;
    }

    private static void Detach(Player player)
    {
        player.PropertiesCts?.Cancel();
        if (player.Session is not { } session) return;
        player.Session = null;
        try
        {
            session.MediaPropertiesChanged -= player.OnProperties;
            session.PlaybackInfoChanged -= player.OnPlayback;
            session.TimelinePropertiesChanged -= player.OnTimeline;
        }
        catch (Exception)
        {
            // Already closed.
        }
    }

    private void OnMediaPropertiesChanged(Player player)
    {
        // Players often send several updates in a row (title first, then artist, then artwork).
        CancellationTokenSource cts;
        lock (gate)
        {
            if (!players.Contains(player)) return;
            player.PropertiesCts?.Cancel();
            player.PropertiesCts = cts = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PropertiesDebounce, cts.Token);
                await ReadPropertiesAsync(player, cts.Token);
                Choose();
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

    private void OnTimelinePropertiesChanged(Player player)
    {
        lock (gate)
        {
            if (player.Session is not { } session || !players.Contains(player)) return;

            // The length often arrives after the title, and it decides whether this is a song at all.
            var duration = ReadDuration(session);
            var lengthChanged = duration != player.Track.Duration;
            if (lengthChanged) player.Track = player.Track with { Duration = duration };
            if (!lengthChanged && !ReferenceEquals(player, followed)) return;
        }
        Choose();
    }

    private async Task ReadPropertiesAsync(Player player, CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (gate) session = player.Session;
        if (session is null) return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var props = await session.TryGetMediaPropertiesAsync().AsTask(timeout.Token);
        var track = new TrackInfo(
            props.Artist ?? string.Empty,
            props.Title ?? string.Empty,
            string.IsNullOrWhiteSpace(props.AlbumTitle) ? null : props.AlbumTitle,
            ReadDuration(session));

        lock (gate)
        {
            if (!players.Contains(player)) return;
            player.Track = track;
            player.Thumbnail = props.Thumbnail;
        }
    }

    /// <summary>Decides which player to follow from the current state and reports what changed.</summary>
    private void Choose()
    {
        lock (gate)
        {
            if (disposed || manager is null) return;

            // The object for Windows' own choice is not one of the listed ones either; its app id says which it is.
            string? systemChoice = null;
            try
            {
                systemChoice = manager.GetCurrentSession()?.SourceAppUserModelId;
            }
            catch (Exception)
            {
                // No system choice then; the picker falls back to the first player.
            }

            var snapshots = players
                .Select(p => new PlayerSnapshot(p.App, p.Track, IsPlaying(p.Session), p.App == systemChoice, songsWithoutLyrics.Contains(SongKey(p))))
                .ToList();
            var index = PlayerPicker.Pick(snapshots, followed is null ? -1 : players.IndexOf(followed), pinnedApp, ignoredApps);
            followed = index < 0 ? null : players[index];

            if (followed is null)
            {
                SetTrack(null);
                SetPlayback(PlaybackState.Stopped);
                return;
            }
            SetTrack(new MediaTrack(followed.App, followed.Track, followed.Thumbnail));
            SetPlayback(ReadPlayback(followed.Session));
        }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession? session)
    {
        try
        {
            return session?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static TimeSpan? ReadDuration(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var timeline = session.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;
            return duration > TimeSpan.Zero ? duration : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static PlaybackState ReadPlayback(GlobalSystemMediaTransportControlsSession? session)
    {
        try
        {
            if (session is null) return PlaybackState.Stopped;
            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var now = DateTimeOffset.Now;
            var duration = timeline.EndTime - timeline.StartTime;
            return new PlaybackState(
                info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                timeline.Position - timeline.StartTime,
                PlaybackState.SanitizeUpdatedAt(timeline.LastUpdatedTime, now),
                info.PlaybackRate ?? 1.0,
                duration > TimeSpan.Zero ? duration : null);
        }
        catch (Exception)
        {
            return PlaybackState.Stopped; // Session closed.
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
        // Every event of every player ends up here; only a real change is worth telling.
        if (state == Playback) return;
        Playback = state;
        PlaybackChanged?.Invoke(this, state);
    }

    private static string SongKey(Player player) => $"{player.App}\n{player.Track.Artist}\n{player.Track.Title}";

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
            foreach (var player in players) Detach(player);
            players.Clear();
            followed = null;
            if (manager is not null)
            {
                manager.SessionsChanged -= OnSessionsChanged;
                manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }
        }
    }
}
