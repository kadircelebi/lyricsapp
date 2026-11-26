using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
using LyricsApp.Core.Recognition;
using LyricsApp.Media;

namespace LyricsApp;

public partial class MainWindow : Window
{
    private readonly AppSettings settings = AppSettings.Load();
    private readonly ProviderCatalog providers = new(AppSettings.SettingsFolder);
    private readonly System.Net.Http.HttpClient shazamHttp = ShazamClient.CreateHttpClient();
    private readonly MainViewModel viewModel;

    private readonly System.Windows.Threading.DispatcherTimer hideChromeTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private SettingsWindow? settingsWindow;
    private StreamerWindow? streamerWindow;

    public MainWindow()
    {
        InitializeComponent();

        if (Demo.DemoMode.IsEnabled)
        {
            // Store screenshots: made-up song and lyrics, no network, nothing written to the lyrics folder.
            var demoService = new LyricsService([Demo.DemoMode.CreateLyricsProvider()], cache: null);
            viewModel = new MainViewModel(settings, Demo.DemoMode.CreateMediaSource(), demoService, recognizer: null,
                TrackOffsets.InMemory(), Dispatcher);
        }
        else
        {
            var cache = new LocalFolderProvider(settings.LyricsFolder);
            var service = new LyricsService(providers.Build(settings.EnabledProviders, cache), cache);
            var watcher = new MediaWatcher();
            var recognizer = new ShazamRecognizer(new ShazamClient(shazamHttp),
                ct => LoopbackAudio.ReadAsync(watcher.CurrentTrack?.SourceApp, ct));
            viewModel = new MainViewModel(settings, watcher, service, recognizer, TrackOffsets.Load(AppSettings.OffsetsPath), Dispatcher);
        }
        settings.Display.PropertyChanged += OnDisplayPropertyChanged;
        DataContext = viewModel;
        Lyrics.Attach(viewModel);

        RestoreWindowPlacement();
        hideChromeTimer.Tick += OnHideChromeTick;

        // handledEventsToo: the lyrics ScrollViewer marks left clicks as handled (it takes focus),
        // so a plain MouseLeftButtonDown handler on Root would never see clicks on the text.
        Root.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnRootMouseDown), handledEventsToo: true);

        StateChanged += (_, _) =>
        {
            viewModel.SetWindowVisible(WindowState != WindowState.Minimized);
            Lyrics.IsSuspended = WindowState == WindowState.Minimized;
        };
        Closing += (_, _) => SaveWindowPlacement();
        Closed += (_, _) =>
        {
            Lyrics.Detach();
            viewModel.Dispose();
            providers.Dispose();
            shazamHttp.Dispose();
        };
    }

    /// <summary>
    /// Shows the window the app was last left in (the streamer window if streamer mode is on) and starts watching the player.
    /// </summary>
    public async void Start()
    {
        if (settings.Display.StreamerMode) EnterStreamerMode();
        else Show();
        await viewModel.StartAsync();
    }

    // ---- Streamer mode: this window steps aside for the capture-friendly StreamerWindow ----

    private void EnterStreamerMode()
    {
        if (streamerWindow is not null) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        var window = new StreamerWindow(viewModel) { Width = Width, Height = Height };
        if (double.IsFinite(Left) && double.IsFinite(Top))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = Left;
            window.Top = Top;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        window.Closed += OnStreamerWindowClosed;
        streamerWindow = window;
        window.Show();

        // An owned window hides with its owner, so an open settings window moves over to the new one.
        if (settingsWindow is { IsLoaded: true }) settingsWindow.Owner = window;
        Hide();
    }

    private void LeaveStreamerMode()
    {
        if (streamerWindow is not { } window) return;
        streamerWindow = null;
        TakePlacementFrom(window);
        Show();
        if (settingsWindow is { IsLoaded: true }) settingsWindow.Owner = this;
        window.Close();
    }

    /// <summary>Closed by the user (Alt+F4, taskbar) rather than by leaving the mode: the app closes, still in streamer mode.</summary>
    private void OnStreamerWindowClosed(object? sender, EventArgs e)
    {
        if (streamerWindow is null || !ReferenceEquals(sender, streamerWindow)) return;
        TakePlacementFrom(streamerWindow);
        streamerWindow = null;
        Close();
    }

    /// <summary>The streamer window may have been moved; this window comes back where that one was.</summary>
    private void TakePlacementFrom(Window window)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = window.Left;
        Top = window.Top;
    }

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInsideInteractiveControl(e.OriginalSource as DependencyObject)) return;
        if (e.ClickCount == 2)
        {
            // Double-click toggles the lyrics-only (transparent) mode.
            settings.Display.LyricsOnly = !settings.Display.LyricsOnly;
            return;
        }

        // In lyrics-only mode this only fires on the letters themselves (transparent pixels are click-through).
        // Dragging the text moves the window; a click without moving reveals the controls.
        var before = new Point(Left, Top);
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse was released before the drag started.
        }
        if (settings.Display.LyricsOnly && new Point(Left, Top) == before)
        {
            viewModel.IsChromeRevealed = true;
            hideChromeTimer.Stop();
        }
    }

    /// <summary>Buttons, the offset label and the scrollbar keep their own clicks; everything else drags/reveals.</summary>
    private bool IsInsideInteractiveControl(DependencyObject? source)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, Root); d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.ScrollBar
                || ReferenceEquals(d, OffsetLabel))
                return true;
        }
        return false;
    }

    private void OnRootMouseEnter(object sender, MouseEventArgs e) => hideChromeTimer.Stop();

    private void OnRootMouseLeave(object sender, MouseEventArgs e)
    {
        if (viewModel.IsChromeRevealed) hideChromeTimer.Start();
    }

    private void OnHideChromeTick(object? sender, EventArgs e)
    {
        // Keep the controls while the right-click menu is open (the mouse is over the menu, not the window).
        if (Root.ContextMenu is { IsOpen: true } || Root.IsMouseOver) return;
        hideChromeTimer.Stop();
        viewModel.IsChromeRevealed = false;
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => _ = viewModel.RetryAsync();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => viewModel.OpenLyricsFolder();

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (settingsWindow is { IsLoaded: true })
        {
            settingsWindow.Activate();
            return;
        }
        settingsWindow = new SettingsWindow(settings) { Owner = this };
        settingsWindow.Closed += (_, _) =>
        {
            settings.Save();
            settingsWindow = null;
        };
        settingsWindow.Show();
    }

    private void OnOffsetMinusClick(object sender, RoutedEventArgs e) => viewModel.AdjustOffset(-0.5);

    private void OnOffsetPlusClick(object sender, RoutedEventArgs e) => viewModel.AdjustOffset(+0.5);

    private void OnOffsetResetClick(object sender, RoutedEventArgs e) => viewModel.ResetOffset();

    private void OnDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.LyricsOnly):
                hideChromeTimer.Stop();
                viewModel.IsChromeRevealed = false;
                break;
            case nameof(DisplaySettings.StreamerMode):
                if (settings.Display.StreamerMode) EnterStreamerMode();
                else LeaveStreamerMode();
                break;
        }
    }

    // ---- Window placement ----

    private void RestoreWindowPlacement()
    {
        Width = settings.Width;
        Height = settings.Height;
        if (settings.Left is { } left && settings.Top is { } top
            && left >= SystemParameters.VirtualScreenLeft && top >= SystemParameters.VirtualScreenTop
            && left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top + 50 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        // Never shown and never placed (the app ran in streamer mode from start to end): keep the saved position.
        if (double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top))
        {
            settings.Left = bounds.Left;
            settings.Top = bounds.Top;
        }
        settings.Width = bounds.Width;
        settings.Height = bounds.Height;
        settings.Save();
    }
}
