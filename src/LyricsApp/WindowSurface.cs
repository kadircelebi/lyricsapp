using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LyricsApp;

/// <summary>
/// Gives a borderless window what AllowsTransparency used to give it, in a way window capture can read:
/// per-pixel transparency and optional click-through.
/// </summary>
/// <remarks>
/// A window with AllowsTransparency is drawn through UpdateLayeredWindow and has no surface of its own, so programs
/// that copy a window with BitBlt (the default in OBS) get a black picture. Here WPF draws on a transparent surface
/// and the desktop compositor treats the whole client area as glass: whatever is not drawn shows the desktop, and
/// the window keeps a normal surface that both capture methods can read.
/// </remarks>
public sealed class WindowSurface
{
    private readonly Window window;
    private IntPtr handle;
    private IntPtr previousWindowProc;
    private NativeMethods.WindowProc? windowProc;
    private bool isClickThrough;

    public WindowSurface(Window window)
    {
        this.window = window;
        window.SourceInitialized += (_, _) => Attach();
    }

    public IntPtr Handle => handle;

    /// <summary>Mouse input goes to whatever is behind the window, as if it were not there.</summary>
    public bool IsClickThrough
    {
        get => isClickThrough;
        set
        {
            if (isClickThrough == value) return;
            isClickThrough = value;
            ApplyStyles();
        }
    }

    private void Attach()
    {
        handle = new WindowInteropHelper(window).Handle;
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins);

        // Windows sends mouse input past a window that is both layered and transparent. WPF removes the layered
        // style from windows without AllowsTransparency every time the styles change, so this procedure, placed
        // in front of WPF's, puts it back. Layered this way (one opacity for the whole window, fully opaque) the
        // window still draws and captures like a normal one; only the transparent style is switched on and off.
        windowProc = WindowProc;
        previousWindowProc = NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlpWndProc, Marshal.GetFunctionPointerForDelegate(windowProc));
        ApplyStyles();
        NativeMethods.SetLayeredWindowAttributes(handle, 0, 255, NativeMethods.LwaAlpha);
    }

    private void ApplyStyles()
    {
        if (handle == IntPtr.Zero) return;
        var styles = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        var wanted = WantedStyles(styles);
        if (wanted != styles) NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(wanted));
    }

    private long WantedStyles(long styles)
    {
        styles |= NativeMethods.WsExLayered;
        return isClickThrough ? styles | NativeMethods.WsExTransparent : styles & ~NativeMethods.WsExTransparent;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
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
            var styles = (uint)Marshal.ReadInt32(lParam, styleNewOffset);
            Marshal.WriteInt32(lParam, styleNewOffset, (int)(uint)WantedStyles(styles));
        }
        return result;
    }

    /// <summary>Moves and sizes the window in one step, in screen pixels.</summary>
    public void SetBounds(Rect pixels)
    {
        const uint noZOrder = 0x0004, noActivate = 0x0010;
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(pixels.Left), (int)Math.Round(pixels.Top),
            (int)Math.Round(pixels.Width), (int)Math.Round(pixels.Height), noZOrder | noActivate);
    }

    /// <summary>
    /// The bounds of a window after the given edges were dragged by (<paramref name="dx"/>, <paramref name="dy"/>).
    /// The opposite edges stay where they are; the window never gets smaller than the minimum size.
    /// </summary>
    public static Rect Resize(Rect start, bool left, bool top, bool right, bool bottom, double dx, double dy, double minWidth, double minHeight)
    {
        double x = start.Left, y = start.Top, width = start.Width, height = start.Height;
        if (left)
        {
            width = Math.Max(minWidth, start.Width - dx);
            x = start.Right - width;
        }
        else if (right)
        {
            width = Math.Max(minWidth, start.Width + dx);
        }
        if (top)
        {
            height = Math.Max(minHeight, start.Height - dy);
            y = start.Bottom - height;
        }
        else if (bottom)
        {
            height = Math.Max(minHeight, start.Height + dy);
        }
        return new Rect(x, y, width, height);
    }
}
