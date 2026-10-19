using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
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
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer syncTimer;

    private CancellationTokenSource? searchCts;
    private IReadOnlyList<LyricLine> syncedLines = [];
    private PlaybackState playback = PlaybackState.Stopped;
    private TrackInfo? currentQuery;
    private bool isWindowVisible = true;

    private string title = Strings.WaitingForTrack;
    private string artist = string.Empty;
    private string status = Strings.StartPlaying;
    private string providerName = string.Empty;
    private ImageSource? thumbnail;
    private bool isSynced;
    private int activeIndex = -1;

    public MainViewModel(AppSettings settings, IMediaSource watcher, LyricsService lyricsService, Dispatcher dispatcher)
    {
        this.settings = settings;
        this.watcher = watcher;
        this.lyricsService = lyricsService;
        this.dispatcher = dispatcher;

        // Runs only while synced lyrics are on screen and the player is playing.
        syncTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        syncTimer.Tick += (_, _) => UpdateActiveLine();

        watcher.TrackChanged += (_, track) => dispatcher.InvokeAsync(() => OnTrackChanged(track));
        watcher.PlaybackChanged += (_, state) => dispatcher.InvokeAsync(() => OnPlaybackChanged(state));

        settings.Display.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DisplaySettings.FocusMode) or nameof(DisplaySettings.ContextLines))
            {
                OnPropertyChanged(nameof(IsFocusView));
                UpdateLineStates();
            }
        };
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
        var position = playback.GetPosition(DateTimeOffset.Now) + TimeSpan.FromSeconds(settings.OffsetSeconds);
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

    public string OffsetText => Strings.OffsetSeconds(settings.OffsetSeconds == 0
        ? "0"
        : settings.OffsetSeconds.ToString("+0.0;-0.0", CultureInfo.CurrentCulture));

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

    public void AdjustOffset(double deltaSeconds)
    {
        settings.OffsetSeconds = Math.Round(settings.OffsetSeconds + deltaSeconds, 1);
        if (Math.Abs(settings.OffsetSeconds) < 0.05) settings.OffsetSeconds = 0;
        OnPropertyChanged(nameof(OffsetText));
        UpdateActiveLine();
    }

    public void ResetOffset() => AdjustOffset(-settings.OffsetSeconds);

    public Task RetryAsync() => currentQuery is null ? watcher.RefreshAsync() : SearchAsync(currentQuery);

    public void OpenLyricsFolder()
    {
        Directory.CreateDirectory(settings.LyricsFolder);
        Process.Start(new ProcessStartInfo(settings.LyricsFolder) { UseShellExecute = true });
    }

    private async void OnTrackChanged(MediaTrack? track)
    {
        searchCts?.Cancel();
        ClearLyrics();

        if (track is null)
        {
            currentQuery = null;
            Title = Strings.WaitingForTrack;
            Artist = string.Empty;
            Thumbnail = null;
            Status = Strings.StartPlaying;
            return;
        }

        var cleaned = TrackTextCleaner.Clean(track.Track);
        currentQuery = cleaned;
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
                break;
        }
        ProviderName = search.Result is null ? string.Empty : DisplaySource(search.Result.Lyrics.Source);
        UpdateLineStates();
        UpdateTimer();
        UpdateActiveLine();
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
        SetActiveIndex(-1);
        UpdateTimer();
    }

    private void OnPlaybackChanged(PlaybackState state)
    {
        var wasPlaying = playback.IsPlaying;
        playback = state;
        if (wasPlaying != state.IsPlaying) OnPropertyChanged(nameof(IsPlaying));
        UpdateTimer();
        UpdateActiveLine();
    }

    private void UpdateTimer()
    {
        var shouldRun = IsSynced && playback.IsPlaying && isWindowVisible;
        if (shouldRun && !syncTimer.IsEnabled) syncTimer.Start();
        else if (!shouldRun && syncTimer.IsEnabled) syncTimer.Stop();
    }

    private void UpdateActiveLine()
    {
        if (!IsSynced || syncedLines.Count == 0) return;
        var position = playback.GetPosition(DateTimeOffset.Now) + TimeSpan.FromSeconds(settings.OffsetSeconds);
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
        watcher.Dispose();
    }
}
