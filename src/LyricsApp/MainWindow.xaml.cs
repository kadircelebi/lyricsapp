using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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

    private DateTime lastUserScroll = DateTime.MinValue;
    private double scrollFrom, scrollTo;
    private DateTime scrollStart;
    private bool scrollAnimating;

    public MainWindow()
    {
        InitializeComponent();

        var cache = new LocalFolderProvider(settings.LyricsFolder);
        var service = new LyricsService(providers.Build(settings.EnabledProviders, cache), cache);
        viewModel = new MainViewModel(settings, new MediaWatcher(), service, Dispatcher);
        viewModel.ActiveLineChanged += (_, index) => ScrollToLine(index);
        DataContext = viewModel;

        RestoreWindowPlacement();
        Topmost = settings.Topmost;
        TopmostToggle.IsChecked = settings.Topmost;

        Loaded += async (_, _) => await viewModel.StartAsync();
        SourceInitialized += (_, _) => UseDarkTitleBar();
        StateChanged += (_, _) => viewModel.SetWindowVisible(WindowState != WindowState.Minimized);
        Closing += (_, _) => SaveWindowPlacement();
        Closed += (_, _) =>
        {
            viewModel.Dispose();
            providers.Dispose();
        };
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => _ = viewModel.RetryAsync();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => viewModel.OpenLyricsFolder();

    private void OnTopmostClick(object sender, RoutedEventArgs e)
    {
        Topmost = settings.Topmost = TopmostToggle.IsChecked == true;
        settings.Save();
    }

    private void OnOffsetMinusClick(object sender, RoutedEventArgs e) => viewModel.AdjustOffset(-0.5);

    private void OnOffsetPlusClick(object sender, RoutedEventArgs e) => viewModel.AdjustOffset(+0.5);

    private void OnOffsetResetClick(object sender, RoutedEventArgs e) => viewModel.ResetOffset();

    private void OnLyricsUserScroll(object sender, RoutedEventArgs e) => lastUserScroll = DateTime.UtcNow;

    /// <summary>Smoothly centres the active line unless the user scrolled by hand a moment ago.</summary>
    private void ScrollToLine(int index)
    {
        if (index < 0)
        {
            if (viewModel.Lines.Count == 0) LyricsScroller.ScrollToTop();
            return;
        }
        if (DateTime.UtcNow - lastUserScroll < ManualScrollHold) return;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void UseDarkTitleBar()
    {
        const int DwmwaUseImmersiveDarkMode = 20;
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
