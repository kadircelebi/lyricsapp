using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LyricsApp;

/// <summary>
/// The window shown in streamer mode: song card and lyrics on a transparent background. Every click goes through
/// to whatever is behind; while the mouse is over it, a small <see cref="StreamerControls"/> window with a move
/// button and a leave button appears in one corner.
/// </summary>
public partial class StreamerWindow : Window
{
    // Distance between the buttons and the window edge.
    private const double ControlsMargin = 6;

    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private StreamerControls? controls;
    private bool controlsAtLeft, controlsAtTop;
    private bool isMoving;

    private IntPtr handle;
    private IntPtr previousWindowProc;
    private NativeMethods.WindowProc? windowProc;

    public StreamerWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Lyrics.Attach(viewModel);

        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            MakeBackgroundTransparent();
            MakeClickThrough();
        };
        Loaded += (_, _) => hoverTimer.Start();
        LocationChanged += (_, _) => PositionControls();
        hoverTimer.Tick += (_, _) => UpdateControlsVisibility();
        Closed += (_, _) =>
        {
            hoverTimer.Stop();
            Lyrics.Detach();
        };
    }

    /// <summary>
    /// Per-pixel transparency without AllowsTransparency: WPF draws on a transparent surface and the desktop
    /// compositor treats the whole client area as glass, so whatever is not drawn shows the desktop.
    /// The window keeps a normal redirection surface, which is what window capture reads.
    /// </summary>
    private void MakeBackgroundTransparent()
    {
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins);
    }

    /// <summary>
    /// Windows sends mouse input past a window that is both layered and transparent. WPF removes the layered style
    /// from windows that do not use AllowsTransparency every time the styles change, so a window procedure placed
    /// in front of WPF's puts the two styles back. Layered this way (one opacity for the whole window, fully opaque)
    /// the window still draws and captures like a normal one.
    /// </summary>
    private void MakeClickThrough()
    {
        windowProc = KeepClickThroughStyles;
        previousWindowProc = NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlpWndProc, Marshal.GetFunctionPointerForDelegate(windowProc));

        var styles = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
            new IntPtr(styles | NativeMethods.WsExLayered | NativeMethods.WsExTransparent));
        NativeMethods.SetLayeredWindowAttributes(handle, 0, 255, NativeMethods.LwaAlpha);
    }

    private IntPtr KeepClickThroughStyles(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WmNcDestroy)
        {
            // Step out first, so WPF finds its own procedure in place when it takes the window down.
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlpWndProc, previousWindowProc);
            return NativeMethods.CallWindowProc(previousWindowProc, hwnd, message, wParam, lParam);
        }

        var result = NativeMethods.CallWindowProc(previousWindowProc, hwnd, message, wParam, lParam);
        if (message == NativeMethods.WmStyleChanging && wParam.ToInt64() == NativeMethods.GwlExStyle)
        {
            // STYLESTRUCT { styleOld, styleNew }: the new value is the second 32-bit field.
            const int styleNewOffset = 4;
            var styles = Marshal.ReadInt32(lParam, styleNewOffset);
            Marshal.WriteInt32(lParam, styleNewOffset, styles | (int)(NativeMethods.WsExLayered | NativeMethods.WsExTransparent));
        }
        return result;
    }

    // ---- The two buttons ----

    /// <summary>
    /// A click-through window gets no mouse messages, so the pointer position is read a few times a second instead
    /// (only while this window is open).
    /// </summary>
    private void UpdateControlsVisibility()
    {
        if (isMoving || !NativeMethods.GetCursorPos(out var cursor) || !NativeMethods.GetWindowRect(handle, out var window)) return;
        var inside = cursor.X >= window.Left && cursor.X < window.Right && cursor.Y >= window.Top && cursor.Y < window.Bottom;
        if (inside && controls is not { IsVisible: true })
        {
            controls ??= new StreamerControls(this) { Owner = this, DataContext = viewModel };
            ChooseControlsCorner();
            PositionControls();
            controls.Show();
        }
        else if (!inside && controls is { IsVisible: true })
        {
            controls.Hide();
        }
    }

    private void ChooseControlsCorner()
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetWindowRect(handle, out var window)
            || !NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest), ref info))
            return;

        static Rect ToRect(NativeMethods.Rect r) => new(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
        (controlsAtLeft, controlsAtTop) = ChooseControlsCorner(ToRect(window), ToRect(info.Work));
    }

    /// <summary>
    /// The corner of the window nearest to the middle of the screen it is on, so the buttons stay within reach
    /// when the window sits against a screen edge.
    /// </summary>
    public static (bool AtLeft, bool AtTop) ChooseControlsCorner(Rect window, Rect workArea)
    {
        var windowIsRight = window.Left + window.Width / 2 > workArea.Left + workArea.Width / 2;
        var windowIsLow = window.Top + window.Height / 2 > workArea.Top + workArea.Height / 2;
        return (windowIsRight, windowIsLow);
    }

    private void PositionControls()
    {
        if (controls is null) return;
        controls.Left = controlsAtLeft ? Left + ControlsMargin : Left + ActualWidth - controls.Width - ControlsMargin;
        controls.Top = controlsAtTop ? Top + ControlsMargin : Top + ActualHeight - controls.Height - ControlsMargin;
    }

    /// <summary>Called by the move button while the left mouse button is down: drags this window until it is released.</summary>
    public void MoveWithMouse()
    {
        isMoving = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse was released before the drag started.
        }
        finally
        {
            isMoving = false;
        }
        ChooseControlsCorner();
        PositionControls();
    }

    public void LeaveStreamerMode() => viewModel.Display.StreamerMode = false;
}
