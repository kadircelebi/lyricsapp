using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LyricsApp;

/// <summary>
/// The lyric lines with their shadow, karaoke sweep and auto-scroll. Each window that shows lyrics
/// hosts one and connects it to the shared view model with <see cref="Attach"/>.
/// </summary>
public partial class LyricsView : UserControl
{
    private static readonly TimeSpan ManualScrollHold = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ScrollAnimationDuration = TimeSpan.FromMilliseconds(300);

    private readonly LinearGradientBrush fillBrush;
    private readonly DropShadowEffect shadow = new() { Direction = 315 };

    private MainViewModel? viewModel;
    private DisplaySettings? display;
    private DateTime lastUserScroll = DateTime.MinValue;
    private double scrollFrom, scrollTo;
    private DateTime scrollStart;
    private bool scrollAnimating;
    private bool fillRendering;
    private bool isSuspended;

    public LyricsView()
    {
        InitializeComponent();

        // Karaoke sweep brush for the active line. Kept out of resource dictionaries (which freeze it)
        // so its stops can be moved every frame; the template binds to the FillBrush property.
        fillBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i < 4; i++) fillBrush.GradientStops.Add(new GradientStop(Colors.White, i == 3 ? 1 : 0));

        IsVisibleChanged += (_, _) =>
        {
            UpdateFillRendering();
            // A view that was hidden while the song went on catches up when it comes back.
            if (IsVisible && viewModel is not null) ScrollToLine(viewModel.ActiveIndex, force: true);
        };
    }

    public Brush FillBrush => fillBrush;

    /// <summary>True while the host window is minimized: nothing is redrawn per frame.</summary>
    public bool IsSuspended
    {
        get => isSuspended;
        set
        {
            isSuspended = value;
            UpdateFillRendering();
        }
    }

    public void Attach(MainViewModel model)
    {
        Detach();
        viewModel = model;
        display = model.Display;
        model.ActiveLineChanged += OnActiveLineChanged;
        model.PropertyChanged += OnViewModelPropertyChanged;
        display.PropertyChanged += OnDisplayPropertyChanged;
        SetUpShadow();
        UpdateFillColors();
        UpdateFillRendering();
    }

    /// <summary>Lets go of the view model, so a closed window is not kept alive by its events.</summary>
    public void Detach()
    {
        StopFillRendering();
        if (scrollAnimating)
        {
            CompositionTarget.Rendering -= OnScrollFrame;
            scrollAnimating = false;
        }
        if (viewModel is null || display is null) return;
        viewModel.ActiveLineChanged -= OnActiveLineChanged;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        display.PropertyChanged -= OnDisplayPropertyChanged;
        BindingOperations.ClearAllBindings(shadow);
        viewModel = null;
        display = null;
    }

    private void OnActiveLineChanged(object? sender, int index) => ScrollToLine(index);

    private void OnLyricsUserScroll(object sender, RoutedEventArgs e) => lastUserScroll = DateTime.UtcNow;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (viewModel is null) return;
        if (e.PropertyName is nameof(MainViewModel.IsSynced) or nameof(MainViewModel.IsPlaying))
            UpdateFillRendering();
        if (e.PropertyName is nameof(MainViewModel.IsFocusView) && !viewModel.IsFocusView)
            ScrollToLine(viewModel.ActiveIndex, force: true);
    }

    private void OnDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (display is null) return;
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.ShadowEnabled):
                LyricsList.Effect = display.ShadowEnabled ? shadow : null;
                break;
            case nameof(DisplaySettings.ActiveColor) or nameof(DisplaySettings.InactiveColor):
                UpdateFillColors();
                break;
            case nameof(DisplaySettings.LineFill):
                UpdateFillRendering();
                break;
        }
    }

    /// <summary>One shadow for the whole lyrics block (cheaper than one effect per line).</summary>
    private void SetUpShadow()
    {
        if (display is null) return;
        void Bind(DependencyProperty property, string path) =>
            BindingOperations.SetBinding(shadow, property, new Binding(path) { Source = display });

        Bind(DropShadowEffect.ColorProperty, nameof(DisplaySettings.ShadowColorValue));
        Bind(DropShadowEffect.BlurRadiusProperty, nameof(DisplaySettings.ShadowBlur));
        Bind(DropShadowEffect.ShadowDepthProperty, nameof(DisplaySettings.ShadowDepth));
        Bind(DropShadowEffect.OpacityProperty, nameof(DisplaySettings.ShadowOpacity));
        LyricsList.Effect = display.ShadowEnabled ? shadow : null;
    }

    // ---- Karaoke sweep: redraws per frame only while it is visible and the song is playing ----

    private void UpdateFillColors()
    {
        if (display is null) return;
        var active = DisplaySettings.ParseColor(display.ActiveColor) ?? Colors.White;
        var inactive = DisplaySettings.ParseColor(display.InactiveColor) ?? Colors.Gray;
        fillBrush.GradientStops[0].Color = active;
        fillBrush.GradientStops[1].Color = active;
        fillBrush.GradientStops[2].Color = inactive;
        fillBrush.GradientStops[3].Color = inactive;
    }

    private void UpdateFillRendering()
    {
        if (viewModel is null || display is null) return;
        var shouldRun = display.LineFill && viewModel.IsSynced && viewModel.IsPlaying && IsVisible && !isSuspended;
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

    private void OnFillFrame(object? sender, EventArgs e)
    {
        if (viewModel is not null) SetFillProgress(viewModel.GetActiveLineProgress());
    }

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
        if (viewModel is null || viewModel.IsFocusView || !IsVisible) return;
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
            if (!container.IsDescendantOf(LyricsList)) return;
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
}
