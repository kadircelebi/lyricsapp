using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
using LyricsApp.Media;

namespace LyricsApp;

public partial class MainWindow : Window
{
    private static readonly TimeSpan ManualScrollHold = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ScrollAnimationDuration = TimeSpan.FromMilliseconds(300);

    private readonly AppSettings settings = AppSettings.Load();
    private readonly ProviderCatalog providers = new(AppSettings.SettingsFolder);
    private readonly MainViewModel viewModel;
    private readonly LinearGradientBrush fillBrush;
    private readonly DropShadowEffect shadow = new() { Direction = 315 };

    private readonly System.Windows.Threading.DispatcherTimer hideChromeTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private SettingsWindow? settingsWindow;
    private DateTime lastUserScroll = DateTime.MinValue;
    private double scrollFrom, scrollTo;
    private DateTime scrollStart;
    private bool scrollAnimating;
    private bool fillRendering;

    public MainWindow()
    {
        InitializeComponent();

        if (Demo.DemoMode.IsEnabled)
        {
            // Store screenshots: made-up song and lyrics, no network, nothing written to the lyrics folder.
            var demoService = new LyricsService([Demo.DemoMode.CreateLyricsProvider()], cache: null);
            viewModel = new MainViewModel(settings, Demo.DemoMode.CreateMediaSource(), demoService, Dispatcher);
        }
        else
        {
            var cache = new LocalFolderProvider(settings.LyricsFolder);
            var service = new LyricsService(providers.Build(settings.EnabledProviders, cache), cache);
            viewModel = new MainViewModel(settings, new MediaWatcher(), service, Dispatcher);
        }
        viewModel.ActiveLineChanged += (_, index) => ScrollToLine(index);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        settings.Display.PropertyChanged += OnDisplayPropertyChanged;
        DataContext = viewModel;

        // Karaoke sweep brush for the active line. Kept out of resource dictionaries (which freeze it)
        // so its stops can be moved every frame; the template binds to the FillBrush property.
        fillBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i < 4; i++) fillBrush.GradientStops.Add(new GradientStop(Colors.White, i == 3 ? 1 : 0));
        SetUpShadow();
        UpdateFillColors();

        RestoreWindowPlacement();
        hideChromeTimer.Tick += OnHideChromeTick;

        // handledEventsToo: the lyrics ScrollViewer marks left clicks as handled (it takes focus),
        // so a plain MouseLeftButtonDown handler on Root would never see clicks on the text.
        Root.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnRootMouseDown), handledEventsToo: true);

        Loaded += async (_, _) => await viewModel.StartAsync();
        StateChanged += (_, _) =>
        {
            viewModel.SetWindowVisible(WindowState != WindowState.Minimized);
            UpdateFillRendering();
        };
        Closing += (_, _) => SaveWindowPlacement();
        Closed += (_, _) =>
        {
            StopFillRendering();
            viewModel.Dispose();
            providers.Dispose();
        };
    }

    public Brush FillBrush => fillBrush;

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
        settingsWindow = new SettingsWindow(settings.Display) { Owner = this };
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

    private void OnLyricsUserScroll(object sender, RoutedEventArgs e) => lastUserScroll = DateTime.UtcNow;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsSynced) or nameof(MainViewModel.IsPlaying))
            UpdateFillRendering();
        if (e.PropertyName is nameof(MainViewModel.IsFocusView) && !viewModel.IsFocusView)
            ScrollToLine(viewModel.ActiveIndex, force: true);
    }

    private void OnDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.ShadowEnabled):
                LyricsList.Effect = settings.Display.ShadowEnabled ? shadow : null;
                break;
            case nameof(DisplaySettings.ActiveColor) or nameof(DisplaySettings.InactiveColor):
                UpdateFillColors();
                break;
            case nameof(DisplaySettings.LineFill):
                UpdateFillRendering();
                break;
            case nameof(DisplaySettings.LyricsOnly):
                hideChromeTimer.Stop();
                viewModel.IsChromeRevealed = false;
                break;
        }
    }

    /// <summary>One shadow for the whole lyrics block (cheaper than one effect per line).</summary>
    private void SetUpShadow()
    {
        void Bind(DependencyProperty property, string path) =>
            BindingOperations.SetBinding(shadow, property, new Binding(path) { Source = settings.Display });

        Bind(DropShadowEffect.ColorProperty, nameof(DisplaySettings.ShadowColorValue));
        Bind(DropShadowEffect.BlurRadiusProperty, nameof(DisplaySettings.ShadowBlur));
        Bind(DropShadowEffect.ShadowDepthProperty, nameof(DisplaySettings.ShadowDepth));
        Bind(DropShadowEffect.OpacityProperty, nameof(DisplaySettings.ShadowOpacity));
        LyricsList.Effect = settings.Display.ShadowEnabled ? shadow : null;
    }

    // ---- Karaoke sweep: redraws per frame only while it is visible and the song is playing ----

    private void UpdateFillColors()
    {
        var active = DisplaySettings.ParseColor(settings.Display.ActiveColor) ?? Colors.White;
        var inactive = DisplaySettings.ParseColor(settings.Display.InactiveColor) ?? Colors.Gray;
        fillBrush.GradientStops[0].Color = active;
        fillBrush.GradientStops[1].Color = active;
        fillBrush.GradientStops[2].Color = inactive;
        fillBrush.GradientStops[3].Color = inactive;
    }

    private void UpdateFillRendering()
    {
        var shouldRun = settings.Display.LineFill && viewModel.IsSynced && viewModel.IsPlaying && WindowState != WindowState.Minimized;
        if (shouldRun && !fillRendering)
        {
            fillRendering = true;
            CompositionTarget.Rendering += OnFillFrame;
        }
        else if (!shouldRun)
        {
            StopFillRendering();
            SetFillProgress(viewModel.GetActiveLineProgress());
        }
    }

    private void StopFillRendering()
    {
        if (!fillRendering) return;
        CompositionTarget.Rendering -= OnFillFrame;
        fillRendering = false;
    }

    private void OnFillFrame(object? sender, EventArgs e) => SetFillProgress(viewModel.GetActiveLineProgress());

    private void SetFillProgress(double progress)
    {
        // A soft edge looks more like a sweep than a hard cut.
        const double edge = 0.04;
        var p = Math.Clamp(progress, 0, 1);
        fillBrush.GradientStops[1].Offset = Math.Max(0, p - edge / 2);
        fillBrush.GradientStops[2].Offset = Math.Min(1, p + edge / 2);
    }

    // ---- Auto-scroll for the full (non-focus) view ----

    /// <summary>Smoothly centres the active line unless the user scrolled by hand a moment ago.</summary>
    private void ScrollToLine(int index, bool force = false)
    {
        if (viewModel.IsFocusView) return;
        if (index < 0)
        {
            if (viewModel.Lines.Count == 0) LyricsScroller.ScrollToTop();
            return;
        }
        if (!force && DateTime.UtcNow - lastUserScroll < ManualScrollHold) return;

        // Wait for layout so the container exists and has its final (active) size.
        Dispatcher.InvokeAsync(() =>
        {
            if (LyricsList.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container) return;
            var top = container.TransformToAncestor(LyricsList).Transform(new Point(0, 0)).Y + LyricsList.Margin.Top;
            var target = top + container.ActualHeight / 2 - LyricsScroller.ViewportHeight / 2;
            target = Math.Clamp(target, 0, LyricsScroller.ScrollableHeight);

            scrollFrom = LyricsScroller.VerticalOffset;
            scrollTo = target;
            scrollStart = DateTime.UtcNow;
            if (!scrollAnimating)
            {
                scrollAnimating = true;
                CompositionTarget.Rendering += OnScrollFrame;
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnScrollFrame(object? sender, EventArgs e)
    {
        var t = Math.Min(1.0, (DateTime.UtcNow - scrollStart) / ScrollAnimationDuration);
        var eased = 1 - Math.Pow(1 - t, 3);
        LyricsScroller.ScrollToVerticalOffset(scrollFrom + (scrollTo - scrollFrom) * eased);
        if (t >= 1.0)
        {
            CompositionTarget.Rendering -= OnScrollFrame;
            scrollAnimating = false;
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
        settings.Left = bounds.Left;
        settings.Top = bounds.Top;
        settings.Width = bounds.Width;
        settings.Height = bounds.Height;
        settings.Save();
    }
}
