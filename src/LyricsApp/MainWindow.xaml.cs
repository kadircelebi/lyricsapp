using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
using LyricsApp.Core.Recognition;
using LyricsApp.Media;

namespace LyricsApp;

public partial class MainWindow : Window
{
    // How often the pointer position is read while a mode needs it (see UpdatePointerTracking).
    private static readonly TimeSpan LyricsOnlyPointerInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan StreamerPointerInterval = TimeSpan.FromMilliseconds(100);

    // Distance between the streamer buttons and the window edge.
    private const double ControlsMargin = 6;

    // After the window comes up it is drawn in full again at these moments (see WindowSurface.Repaint).
    private static readonly TimeSpan[] StartupRepaints = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    private readonly AppSettings settings = AppSettings.Load();
    private readonly ProviderCatalog providers = new(AppSettings.SettingsFolder);
    private readonly System.Net.Http.HttpClient shazamHttp = ShazamClient.CreateHttpClient();
    private readonly MainViewModel viewModel;
    private readonly WindowSurface surface;

    private readonly DispatcherTimer hideChromeTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer pointerTimer = new();
    private SettingsWindow? settingsWindow;
    private StreamerControls? streamerControls;
    private bool controlsAtLeft, controlsAtTop;
    private bool isDraggedByControls;
    private FrameworkElement? resizeGrip;
    private NativeMethods.Point resizeStartCursor;
    private NativeMethods.Rect resizeStartBounds;

    public MainWindow()
    {
        InitializeComponent();
        surface = new WindowSurface(this);

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
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        settings.Display.PropertyChanged += OnDisplayPropertyChanged;
        DataContext = viewModel;
        Lyrics.Attach(viewModel);

        RestoreWindowPlacement();
        hideChromeTimer.Tick += OnHideChromeTick;
        pointerTimer.Tick += (_, _) => OnPointerTick();

        // handledEventsToo: the lyrics ScrollViewer marks left clicks as handled (it takes focus),
        // so a plain MouseLeftButtonDown handler on Root would never see clicks on the text.
        Root.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnRootMouseDown), handledEventsToo: true);

        Loaded += async (_, _) =>
        {
            UpdatePointerTracking();
            await viewModel.StartAsync();
        };
        ContentRendered += (_, _) =>
        {
            surface.Repaint();
            foreach (var delay in StartupRepaints)
            {
                var timer = new DispatcherTimer { Interval = delay };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    surface.Repaint();
                };
                timer.Start();
            }
        };
        StateChanged += (_, _) =>
        {
            viewModel.SetWindowVisible(WindowState != WindowState.Minimized);
            Lyrics.IsSuspended = WindowState == WindowState.Minimized;
            UpdatePointerTracking();
        };
        LocationChanged += (_, _) => PositionStreamerControls();
        SizeChanged += (_, _) => PositionStreamerControls();
        Closing += (_, _) => SaveWindowPlacement();
        Closed += (_, _) =>
        {
            pointerTimer.Stop();
            Lyrics.Detach();
            viewModel.Dispose();
            providers.Dispose();
            shazamHttp.Dispose();
        };
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

        // In lyrics-only mode this only fires on the letters themselves (elsewhere the clicks go through).
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

    /// <summary>Buttons, the offset label, the scrollbar and the resize edges keep their own clicks; everything else drags/reveals.</summary>
    private bool IsInsideInteractiveControl(DependencyObject? source)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, Root); d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.ScrollBar
                || ReferenceEquals(d, OffsetLabel) || ReferenceEquals(d, ResizeGrips))
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
        settingsWindow = new SettingsWindow(settings, viewModel.Players, viewModel.ApplySourceSettings) { Owner = this };
        settingsWindow.Closed += (_, _) =>
        {
            settings.Save();
            settingsWindow = null;
        };
        settingsWindow.Show();
    }

    /// <summary>
    /// Source menu: "Automatic" plus the players reporting a song right now; the hand-picked one is ticked and
    /// listed even while it is closed.
    /// </summary>
    private void OnWindowMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = ((ContextMenu)sender).Items.OfType<MenuItem>().FirstOrDefault(item => item.Tag as string == "Source");
        if (menu is null) return;

        menu.Items.Clear();
        foreach (var entry in SourceMenu.Entries(viewModel.Players, viewModel.PinnedApp))
        {
            if (menu.Items.Count == 1) menu.Items.Add(new Separator());
            var item = new MenuItem { Header = entry.Header, IsCheckable = true, IsChecked = entry.IsChecked };
            item.Click += (_, _) =>
            {
                viewModel.PinnedApp = entry.App;
                settings.Save();
            };
            menu.Items.Add(item);
        }
    }

    private void OnVersionNoticeDismissClick(object sender, RoutedEventArgs e) => viewModel.DismissVersionNotice();

    private void OnOffsetMinusClick(object sender, RoutedEventArgs e) => viewModel.AdjustOffset(-0.5);

    private void OnOffsetPlusClick(object sender, RoutedEventArgs e) => viewModel.AdjustOffset(+0.5);

    private void OnOffsetResetClick(object sender, RoutedEventArgs e) => viewModel.ResetOffset();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsChromeRevealed)) UpdatePointerTracking();
    }

    private void OnDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.LyricsOnly) or nameof(DisplaySettings.StreamerMode):
                hideChromeTimer.Stop();
                viewModel.IsChromeRevealed = false;
                UpdatePointerTracking();
                break;
        }
    }

    // ---- Click-through modes ----
    //
    // Streamer mode lets every click through; lyrics-only mode lets through the clicks that miss the letters.
    // A click-through window gets no mouse messages, so while one of the two is on the pointer position is read
    // on a timer instead: to show the streamer buttons, or to take the mouse back when it is over a line.
    // No hook is involved, and the timer stops in the normal mode and while the window is minimized.

    private void UpdatePointerTracking()
    {
        var display = settings.Display;
        var streamer = display.StreamerMode;
        var floating = !streamer && display.LyricsOnly && !viewModel.IsChromeRevealed;
        if (!streamer) streamerControls?.Hide();
        // In the modes that let clicks through there is nothing to grab.
        ResizeGrips.Visibility = streamer || floating ? Visibility.Collapsed : Visibility.Visible;

        if (!IsLoaded || WindowState == WindowState.Minimized || !(streamer || floating))
        {
            pointerTimer.Stop();
            surface.IsClickThrough = streamer;
            return;
        }
        pointerTimer.Interval = streamer ? StreamerPointerInterval : LyricsOnlyPointerInterval;
        pointerTimer.Start();
        OnPointerTick();
    }

    private void OnPointerTick()
    {
        if (!NativeMethods.GetCursorPos(out var cursor) || !NativeMethods.GetWindowRect(surface.Handle, out var bounds)) return;
        var inside = cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;

        if (settings.Display.StreamerMode)
        {
            surface.IsClickThrough = true;
            if (isDraggedByControls) return;
            if (inside && streamerControls is not { IsVisible: true })
            {
                streamerControls ??= new StreamerControls(this) { Owner = this, DataContext = viewModel };
                ChooseStreamerControlsCorner();
                PositionStreamerControls();
                streamerControls.Show();
            }
            else if (!inside && streamerControls is { IsVisible: true })
            {
                streamerControls.Hide();
            }
            return;
        }

        // Lyrics-only: leave things as they are in the middle of a click, a drag or the right-click menu.
        if (IsMouseButtonDown() || Root.ContextMenu is { IsOpen: true }) return;
        surface.IsClickThrough = !(inside && IsOverText(new Point(cursor.X, cursor.Y)));
    }

    private static bool IsMouseButtonDown() =>
        NativeMethods.GetAsyncKeyState(NativeMethods.VkLeftButton) < 0 || NativeMethods.GetAsyncKeyState(NativeMethods.VkRightButton) < 0;

    /// <summary>Whether a screen point is on a letter of a lyric line or of the status text.</summary>
    private bool IsOverText(Point screenPoint)
    {
        if (PresentationSource.FromVisual(Root) is null) return false;
        var hit = VisualTreeHelper.HitTest(Root, Root.PointFromScreen(screenPoint))?.VisualHit;
        return hit is TextBlock text && text.GetPositionFromPoint(text.PointFromScreen(screenPoint), snapToText: false) is not null;
    }

    // ---- Streamer mode: the two buttons ----

    /// <summary>
    /// The buttons go to the corner nearest to the middle of the screen the window is on, so they stay within reach
    /// when the window sits against a screen edge.
    /// </summary>
    private void ChooseStreamerControlsCorner()
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetWindowRect(surface.Handle, out var window)
            || !NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(surface.Handle, NativeMethods.MonitorDefaultToNearest), ref info))
            return;

        static Rect ToRect(NativeMethods.Rect r) => new(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
        (controlsAtLeft, controlsAtTop) = StreamerControls.ChooseCorner(ToRect(window), ToRect(info.Work));
    }

    private void PositionStreamerControls()
    {
        if (streamerControls is not { } controls || WindowState != WindowState.Normal) return;
        controls.Left = controlsAtLeft ? Left + ControlsMargin : Left + ActualWidth - controls.Width - ControlsMargin;
        controls.Top = controlsAtTop ? Top + ControlsMargin : Top + ActualHeight - controls.Height - ControlsMargin;
    }

    /// <summary>The move button is held: the buttons stay where they are on the window until it is released.</summary>
    public void OnControlsDragStarted() => isDraggedByControls = true;

    public void OnControlsDragEnded()
    {
        isDraggedByControls = false;
        ChooseStreamerControlsCorner();
        PositionStreamerControls();
    }

    public void LeaveStreamerMode() => settings.Display.StreamerMode = false;

    // ---- Resizing by the edges (the window has no frame that would do it) ----

    private void OnResizeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is not FrameworkElement { Tag: string } grip || WindowState != WindowState.Normal
            || !NativeMethods.GetCursorPos(out resizeStartCursor) || !NativeMethods.GetWindowRect(surface.Handle, out resizeStartBounds)
            || !grip.CaptureMouse())
            return;
        resizeGrip = grip;
        e.Handled = true;
    }

    private void OnResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (resizeGrip is not { IsMouseCaptured: true, Tag: string edges } || !NativeMethods.GetCursorPos(out var cursor)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var start = new Rect(resizeStartBounds.Left, resizeStartBounds.Top,
            resizeStartBounds.Right - resizeStartBounds.Left, resizeStartBounds.Bottom - resizeStartBounds.Top);
        // Everything in screen pixels, and one step per move, so the far edges do not shake.
        surface.SetBounds(WindowSurface.Resize(start,
            edges.Contains("Left"), edges.Contains("Top"), edges.Contains("Right"), edges.Contains("Bottom"),
            cursor.X - resizeStartCursor.X, cursor.Y - resizeStartCursor.Y,
            MinWidth * dpi.DpiScaleX, MinHeight * dpi.DpiScaleY));
    }

    private void OnResizeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (resizeGrip is null) return;
        resizeGrip.ReleaseMouseCapture();
        resizeGrip = null;
        e.Handled = true;
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
        settings.Left = bounds.Left;
        settings.Top = bounds.Top;
        settings.Width = bounds.Width;
        settings.Height = bounds.Height;
        settings.Save();
    }
}
