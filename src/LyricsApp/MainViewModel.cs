using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricsApp.Core;
using LyricsApp.Media;

namespace LyricsApp;

public sealed class LineViewModel(TimeSpan? time, string text) : ObservableObject
{
    private bool isActive;

    public TimeSpan? Time { get; } = time;
    public string Text { get; } = string.IsNullOrWhiteSpace(text) ? "♪" : text;

    public bool IsActive
    {
        get => isActive;
        set => Set(ref isActive, value);
    }
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly MediaWatcher watcher;
    private readonly LyricsService lyricsService;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer syncTimer;

    private CancellationTokenSource? searchCts;
    private IReadOnlyList<LyricLine> syncedLines = [];
    private PlaybackState playback = PlaybackState.Stopped;
    private TrackInfo? currentQuery;
    private bool isWindowVisible = true;

    private string title = "Şarkı bekleniyor";
    private string artist = string.Empty;
    private string status = "Bir uygulamada müzik çalmaya başlayın.";
    private string providerName = string.Empty;
    private ImageSource? thumbnail;
    private bool isSynced;
    private int activeIndex = -1;

    public MainViewModel(AppSettings settings, MediaWatcher watcher, LyricsService lyricsService, Dispatcher dispatcher)
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
    public bool IsSynced { get => isSynced; private set => Set(ref isSynced, value); }
    public int ActiveIndex => activeIndex;

    public string OffsetText => settings.OffsetSeconds == 0
        ? "0 sn"
        : settings.OffsetSeconds.ToString("+0.0;-0.0", CultureInfo.CurrentCulture) + " sn";

    public async Task StartAsync()
    {
        try
        {
            await watcher.StartAsync();
        }
        catch (Exception ex)
        {
            Status = "Windows medya oturumuna bağlanılamadı: " + ex.Message;
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
            Title = "Şarkı bekleniyor";
            Artist = string.Empty;
            Thumbnail = null;
            Status = "Bir uygulamada müzik çalmaya başlayın.";
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
        Status = "Sözler aranıyor…";

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
                Status = "Enstrümantal parça.";
                break;
            default:
                Status = search.Errors.Count > 0
                    ? "Sözler bulunamadı (bağlantı hatası olabilir)."
                    : "Bu şarkı için söz bulunamadı.";
                break;
        }
        ProviderName = search.Result is null ? string.Empty : search.Result.Lyrics.Source;
        UpdateTimer();
        UpdateActiveLine();
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
        playback = state;
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
        ActiveLineChanged?.Invoke(this, index);
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
