using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
using LyricsApp.Core.Recognition;
using LyricsApp.Media;

namespace LyricsApp;

public sealed class LineViewModel(TimeSpan? time, string text) : ObservableObject
{
    private bool isActive;
    private bool isHidden;
    private double emphasis = 1.0;

    public TimeSpan? Time { get; } = time;
    public string Text { get; } = string.IsNullOrWhiteSpace(text) ? "♪" : text;

    public bool IsActive
    {
        get => isActive;
        set => Set(ref isActive, value);
    }

    /// <summary>Outside the karaoke focus window.</summary>
    public bool IsHidden
    {
        get => isHidden;
        set => Set(ref isHidden, value);
    }

    /// <summary>Target opacity: 1 for the active line, fading with distance in focus mode.</summary>
    public double Emphasis
    {
        get => emphasis;
        set => Set(ref emphasis, value);
    }
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly IMediaSource watcher;
    private readonly LyricsService lyricsService;
    private readonly ITrackRecognizer? recognizer;
    private readonly TrackOffsets offsets;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer syncTimer;
    private readonly DispatcherTimer progressTimer;

    private CancellationTokenSource? searchCts;
    private IReadOnlyList<LyricLine> syncedLines = [];
    private PlaybackState playback = PlaybackState.Stopped;
    private TrackInfo? currentQuery;
    private bool isWindowVisible = true;

    // Added to the player position; positive shows lines earlier. Saved per song in offsets.
    private double offsetSeconds;

    // Songs already sent to recognition this run, so a song Shazam cannot name is not listened to again and again.
    private readonly HashSet<string> recognitionTried = [];
    private TrackInfo? pendingRecognition;

    private string title = Strings.WaitingForTrack;
    private string artist = string.Empty;
    private string status = Strings.StartPlaying;
    private string providerName = string.Empty;
    private ImageSource? thumbnail;
    private bool isSynced;
    private int activeIndex = -1;

    /// <param name="recognizer">Last resort when no source has the lyrics; null turns recognition off (demo mode).</param>
    /// <param name="offsets">Per-song time offsets; a song comes back with its own offset, or none.</param>
    public MainViewModel(AppSettings settings, IMediaSource watcher, LyricsService lyricsService, ITrackRecognizer? recognizer,
        TrackOffsets offsets, Dispatcher dispatcher)
    {
        this.settings = settings;
        this.watcher = watcher;
        this.lyricsService = lyricsService;
        this.recognizer = recognizer;
        this.offsets = offsets;
        this.dispatcher = dispatcher;

        // Runs only while synced lyrics are on screen and the player is playing.
        syncTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        syncTimer.Tick += (_, _) => UpdateActiveLine();

        // Song progress (bar and elapsed time) moves slowly: a few steps a second are plenty. Runs only while it is on screen.
        progressTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        progressTimer.Tick += (_, _) => UpdateSongProgress();

        watcher.TrackChanged += (_, track) => dispatcher.InvokeAsync(() => OnTrackChanged(track));
        watcher.PlaybackChanged += (_, state) => dispatcher.InvokeAsync(() => OnPlaybackChanged(state));

        settings.Display.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DisplaySettings.FocusMode) or nameof(DisplaySettings.ContextLines))
            {
                OnPropertyChanged(nameof(IsFocusView));
                UpdateLineStates();
            }
            if (e.PropertyName is nameof(DisplaySettings.StreamerMode) or nameof(DisplaySettings.StreamerProgress))
            {
                UpdateTimer();
                UpdateSongProgress();
            }
            if (e.PropertyName is nameof(DisplaySettings.VersionNotice)) UpdateVersionNotice();
        };
        ApplySourceSettings();
    }

    public DisplaySettings Display => settings.Display;

    private bool isChromeRevealed;

    /// <summary>In lyrics-only mode: header and background temporarily shown after the text was clicked.</summary>
    public bool IsChromeRevealed
    {
        get => isChromeRevealed;
        set => Set(ref isChromeRevealed, value);
    }

    /// <summary>Karaoke focus applies only to synced lyrics; plain lyrics always show in full.</summary>
    public bool IsFocusView => IsSynced && Display.FocusMode;

    public bool IsPlaying => playback.IsPlaying;

    /// <summary>0..1 progress through the active line, for the karaoke sweep.</summary>
    public double GetActiveLineProgress()
    {
        if (!IsSynced || activeIndex < 0 || activeIndex >= syncedLines.Count) return 0;
        var position = playback.GetPosition(DateTimeOffset.Now) + TimeSpan.FromSeconds(offsetSeconds);
        var start = syncedLines[activeIndex].Time;
        var end = activeIndex + 1 < syncedLines.Count
            ? syncedLines[activeIndex + 1].Time
            : start + TimeSpan.FromSeconds(4);
        var length = (end - start).TotalSeconds;
        // Long instrumental gaps: finish the sweep within a few seconds instead of crawling.
        length = Math.Min(length, 8);
        return length <= 0 ? 1 : Math.Clamp((position - start).TotalSeconds / length, 0, 1);
    }

    /// <summary>Raised when the highlighted line moves, so the view can scroll to it.</summary>
    public event EventHandler<int>? ActiveLineChanged;

    public ObservableCollection<LineViewModel> Lines { get; } = [];

    public string Title { get => title; private set => Set(ref title, value); }
    public string Artist { get => artist; private set => Set(ref artist, value); }
    public string Status { get => status; private set { Set(ref status, value); OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public string ProviderName { get => providerName; private set => Set(ref providerName, value); }
    public ImageSource? Thumbnail { get => thumbnail; private set => Set(ref thumbnail, value); }
    public bool IsSynced
    {
        get => isSynced;
        private set
        {
            if (Set(ref isSynced, value)) OnPropertyChanged(nameof(IsFocusView));
        }
    }
    public int ActiveIndex => activeIndex;

    private double songProgress;
    private bool hasSongProgress;

    /// <summary>How far through the song the player is, from 0 to 1 (the lyrics offset plays no part in it).</summary>
    public double SongProgress { get => songProgress; private set => Set(ref songProgress, value); }

    /// <summary>False when nothing is playing or the player does not report the song's length.</summary>
    public bool HasSongProgress { get => hasSongProgress; private set => Set(ref hasSongProgress, value); }

    private string songPositionText = string.Empty;
    private string songDurationText = string.Empty;

    /// <summary>Time played so far, as a player shows it (1:23).</summary>
    public string SongPositionText { get => songPositionText; private set => Set(ref songPositionText, value); }

    /// <summary>The song's length (3:45).</summary>
    public string SongDurationText { get => songDurationText; private set => Set(ref songDurationText, value); }

    private TimeSpan? lyricsLength;
    private bool showVersionNotice;

    /// <summary>
    /// The player's recording is longer or shorter than the one the lyrics were timed for, so the lines may run early
    /// or late. Stays false once the user turned the note off.
    /// </summary>
    public bool ShowVersionNotice { get => showVersionNotice; private set => Set(ref showVersionNotice, value); }

    /// <summary>The note's "don't show again": off for every song from now on (the settings window turns it back on).</summary>
    public void DismissVersionNotice()
    {
        Display.VersionNotice = false;
        settings.Save();
    }

    private void UpdateVersionNotice() =>
        ShowVersionNotice = Display.VersionNotice && IsSynced && TrackMatcher.IsDifferentVersion(playback.Duration, lyricsLength);

    /// <summary>The players that report a song right now, for the source menu.</summary>
    public IReadOnlyList<MediaPlayerInfo> Players => watcher.Players;

    /// <summary>
    /// The player picked by hand in the source menu; null while the choice is automatic. It lives in the settings,
    /// so it holds across restarts and while that player is closed.
    /// </summary>
    public SavedApp? PinnedApp
    {
        get => settings.PinnedApp;
        set
        {
            settings.PinnedApp = value;
            watcher.PinnedApp = value?.Id;
        }
    }

    /// <summary>Hands the "never follow" list and the hand-picked player from the settings to the media source.</summary>
    public void ApplySourceSettings()
    {
        // A player that is switched off cannot stay the chosen one.
        if (settings.PinnedApp is { } pinned && settings.IgnoredApps.Any(app => app.Id == pinned.Id)) settings.PinnedApp = null;
        watcher.SetIgnoredApps(settings.IgnoredApps.Select(app => app.Id));
        watcher.PinnedApp = settings.PinnedApp?.Id;
    }

    public string OffsetText => Strings.OffsetSeconds(offsetSeconds == 0
        ? "0"
        : offsetSeconds.ToString("+0.0;-0.0", CultureInfo.CurrentCulture));

    public async Task StartAsync()
    {
        try
        {
            await watcher.StartAsync();
        }
        catch (Exception ex)
        {
            Status = Strings.MediaSessionFailed(ex.Message);
        }
    }

    public void SetWindowVisible(bool visible)
    {
        isWindowVisible = visible;
        UpdateTimer();
    }

    /// <summary>Moves the lyrics for the current song and remembers it for that song.</summary>
    public void AdjustOffset(double deltaSeconds)
    {
        SetOffset(offsetSeconds + deltaSeconds);
        offsets.Set(currentQuery, offsetSeconds);
    }

    public void ResetOffset() => AdjustOffset(-offsetSeconds);

    private void SetOffset(double seconds)
    {
        seconds = Math.Round(seconds, 1);
        if (Math.Abs(seconds) < 0.05) seconds = 0;
        if (seconds == offsetSeconds) return;
        offsetSeconds = seconds;
        OnPropertyChanged(nameof(OffsetText));
        UpdateActiveLine();
    }

    public Task RetryAsync()
    {
        if (currentQuery is null) return watcher.RefreshAsync();
        recognitionTried.Remove(RecognitionKey(currentQuery));
        return SearchAsync(currentQuery);
    }

    public void OpenLyricsFolder()
    {
        Directory.CreateDirectory(settings.LyricsFolder);
        Process.Start(new ProcessStartInfo(settings.LyricsFolder) { UseShellExecute = true });
    }

    private async void OnTrackChanged(MediaTrack? track)
    {
        searchCts?.Cancel();
        ClearLyrics();
        pendingRecognition = null;

        if (track is null)
        {
            currentQuery = null;
            UpdateSongProgress();
            SetOffset(0);
            Title = Strings.WaitingForTrack;
            Artist = string.Empty;
            Thumbnail = null;
            Status = Strings.StartPlaying;
            return;
        }

        var cleaned = TrackTextCleaner.Clean(track.Track);
        currentQuery = cleaned;
        // Each song starts with its own saved offset, or none: one song's correction must not carry over.
        SetOffset(offsets.Get(cleaned));
        Title = cleaned.Title;
        Artist = cleaned.Artist;
        Thumbnail = null;
        _ = LoadThumbnailAsync(track);

        await SearchAsync(cleaned);
    }

    private async Task SearchAsync(TrackInfo query)
    {
        searchCts?.Cancel();
        var cts = searchCts = new CancellationTokenSource();
        ClearLyrics();
        Status = Strings.Searching;

        LyricsSearchResult search;
        try
        {
            // Duration often arrives a moment after the title; use it if it is there by now.
            if (query.Duration is null && watcher.CurrentTrack?.Track.Duration is { } duration)
                query = query with { Duration = duration };
            search = await Task.Run(() => lyricsService.FindAsync(query, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested) return;

        foreach (var error in search.Errors) Debug.WriteLine(error);

        if (search.Result is null && CanRecognize(query))
        {
            // Last resort: name the song from its audio. Waits for playback when the player is paused.
            if (playback.IsPlaying) await RecognizeAsync(query);
            else
            {
                pendingRecognition = query;
                Status = search.Errors.Count > 0 ? Strings.NotFoundWithErrors : Strings.NotFound;
            }
            return;
        }
        ShowResult(search, recognized: false);
    }

    // A long recording without an album is a talk, a mix or a film: its sound is not listened to.
    private bool CanRecognize(TrackInfo query) =>
        recognizer is not null && settings.RecognizeUnfoundSongs && !query.IsEmpty && PlayerPicker.IsSong(query)
        && !recognitionTried.Contains(RecognitionKey(query));

    private static string RecognitionKey(TrackInfo track) =>
        $"{TrackMatcher.Normalize(track.Artist)}\n{TrackMatcher.Normalize(track.Title)}";

    // Title equal and artist matching: the recognised name is the one the player already reported.
    private const int SameSongScore = 8;

    /// <summary>Listens to the player for a few seconds, asks Shazam what it is, and searches again under that name.</summary>
    private async Task RecognizeAsync(TrackInfo query)
    {
        if (recognizer is null) return;
        searchCts?.Cancel();
        var cts = searchCts = new CancellationTokenSource();
        recognitionTried.Add(RecognitionKey(query));
        ClearLyrics();
        Status = Strings.Recognizing;

        TrackInfo? recognized;
        try
        {
            recognized = await Task.Run(() => recognizer.RecognizeAsync(cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // Unofficial endpoint, no audio device, capture refused: recognition just gives up.
            Debug.WriteLine($"Recognition: {ex.Message}");
            recognized = null;
        }
        if (cts.IsCancellationRequested) return;

        // Shazam named the same song we already searched for: the sources simply do not have it.
        if (recognized is null || TrackMatcher.Score(query with { Duration = null }, recognized) >= SameSongScore)
        {
            Status = Strings.NotFound;
            watcher.ReportNoLyrics();
            return;
        }

        LyricsSearchResult search;
        try
        {
            // Keep the player's duration: it tells a re-upload's long version apart from the album track.
            var lookup = recognized with { Duration = query.Duration };
            search = await Task.Run(() => lyricsService.FindAsync(lookup, cts.Token, saveAs: query), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested) return;
        foreach (var error in search.Errors) Debug.WriteLine(error);

        if (search.Result is null)
        {
            Status = Strings.RecognizedNotFound(recognized.ToString());
            watcher.ReportNoLyrics();
            return;
        }
        Title = recognized.Title;
        Artist = recognized.Artist;
        ShowResult(search, recognized: true);
    }

    private void ShowResult(LyricsSearchResult search, bool recognized)
    {
        switch (search.Result?.Lyrics)
        {
            case SyncedLyrics synced:
                syncedLines = synced.Lines;
                foreach (var line in synced.Lines) Lines.Add(new LineViewModel(line.Time, line.Text));
                IsSynced = true;
                Status = string.Empty;
                break;
            case PlainLyrics plain:
                foreach (var line in plain.Text.Split(["\r\n", "\n"], StringSplitOptions.None))
                    Lines.Add(new LineViewModel(null, line.Length == 0 ? " " : line));
                Status = string.Empty;
                break;
            case InstrumentalLyrics:
                Status = Strings.Instrumental;
                break;
            default:
                Status = search.Errors.Count > 0
                    ? Strings.NotFoundWithErrors
                    : Strings.NotFound;
                // Lets another player that is playing a song take over (a video was mistaken for one).
                watcher.ReportNoLyrics();
                break;
        }
        var source = search.Result is null ? string.Empty : DisplaySource(search.Result.Lyrics.Source);
        ProviderName = recognized && source.Length > 0 ? Strings.RecognizedSource(source) : source;
        lyricsLength = search.Result?.MatchedTrack.Duration;
        UpdateLineStates();
        UpdateTimer();
        UpdateActiveLine();
        UpdateVersionNotice();
    }

    /// <summary>The local folder reports "Local folder: file.lrc"; that part is shown in the UI language.</summary>
    private static string DisplaySource(string source)
    {
        var prefix = LocalFolderProvider.SourceName + ": ";
        return source.StartsWith(prefix, StringComparison.Ordinal)
            ? Strings.LocalFolderSource(source[prefix.Length..])
            : source;
    }

    private void ClearLyrics()
    {
        syncedLines = [];
        Lines.Clear();
        IsSynced = false;
        ProviderName = string.Empty;
        lyricsLength = null;
        SetActiveIndex(-1);
        UpdateTimer();
        UpdateVersionNotice();
    }

    private void OnPlaybackChanged(PlaybackState state)
    {
        var wasPlaying = playback.IsPlaying;
        playback = state;
        if (wasPlaying != state.IsPlaying) OnPropertyChanged(nameof(IsPlaying));
        UpdateTimer();
        UpdateActiveLine();
        UpdateSongProgress();
        UpdateVersionNotice();

        if (state.IsPlaying && pendingRecognition is { } pending && pending == currentQuery)
        {
            pendingRecognition = null;
            _ = RecognizeAsync(pending);
        }
    }

    private void UpdateTimer()
    {
        var shouldRun = IsSynced && playback.IsPlaying && isWindowVisible;
        if (shouldRun && !syncTimer.IsEnabled) syncTimer.Start();
        else if (!shouldRun && syncTimer.IsEnabled) syncTimer.Stop();

        var showsProgress = Display.StreamerMode && Display.StreamerProgress && playback.IsPlaying && isWindowVisible;
        if (showsProgress && !progressTimer.IsEnabled) progressTimer.Start();
        else if (!showsProgress && progressTimer.IsEnabled) progressTimer.Stop();
    }

    private void UpdateSongProgress()
    {
        var now = DateTimeOffset.Now;
        var progress = currentQuery is null ? null : playback.GetProgress(now);
        HasSongProgress = progress is not null;
        SongProgress = progress ?? 0;
        SongPositionText = progress is null ? string.Empty : SongClock.Format(playback.GetPosition(now));
        SongDurationText = progress is null ? string.Empty : SongClock.Format(playback.Duration ?? TimeSpan.Zero);
    }

    private void UpdateActiveLine()
    {
        if (!IsSynced || syncedLines.Count == 0) return;
        var position = playback.GetPosition(DateTimeOffset.Now) + TimeSpan.FromSeconds(offsetSeconds);
        SetActiveIndex(LrcParser.FindActiveLine(syncedLines, position));
    }

    private void SetActiveIndex(int index)
    {
        if (index == activeIndex) return;
        if (activeIndex >= 0 && activeIndex < Lines.Count) Lines[activeIndex].IsActive = false;
        activeIndex = index;
        if (index >= 0 && index < Lines.Count) Lines[index].IsActive = true;
        UpdateLineStates();
        ActiveLineChanged?.Invoke(this, index);
    }

    /// <summary>Applies the karaoke focus window: hides far lines and fades the near ones by distance.</summary>
    private void UpdateLineStates()
    {
        var focus = IsFocusView;
        var context = Display.ContextLines;
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (!focus)
            {
                line.IsHidden = false;
                line.Emphasis = 1.0;
                continue;
            }

            // Before the first line starts, preview the opening lines as "upcoming".
            var distance = activeIndex < 0 ? i + 1 : Math.Abs(i - activeIndex);
            line.IsHidden = distance > context;
            line.Emphasis = distance == 0 ? 1.0 : Math.Max(0.25, 0.85 - 0.6 * distance / (context + 1.0));
        }
    }

    private async Task LoadThumbnailAsync(MediaTrack track)
    {
        var bytes = await MediaWatcher.ReadThumbnailAsync(track);
        if (bytes is null) return;
        await dispatcher.InvokeAsync(() =>
        {
            if (watcher.CurrentTrack?.Track.Title != track.Track.Title) return;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 128;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();
                Thumbnail = image;
            }
            catch (Exception)
            {
                // Unsupported image format; keep the placeholder.
            }
        });
    }

    public void Dispose()
    {
        searchCts?.Cancel();
        syncTimer.Stop();
        progressTimer.Stop();
        watcher.Dispose();
    }
}
