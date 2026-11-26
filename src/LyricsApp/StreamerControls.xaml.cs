using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LyricsApp;

/// <summary>The move and leave buttons of streamer mode, in a window of their own on top of the click-through one.</summary>
public partial class StreamerControls : Window
{
    private readonly StreamerWindow streamerWindow;

    public StreamerControls(StreamerWindow streamerWindow)
    {
        InitializeComponent();
        this.streamerWindow = streamerWindow;
        SourceInitialized += (_, _) =>
        {
            // Clicking a button must not take the keyboard away from the game or the program being streamed.
            var handle = new WindowInteropHelper(this).Handle;
            var styles = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
                new IntPtr(styles | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
        };
    }

    private void OnMoveMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        streamerWindow.MoveWithMouse();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => streamerWindow.LeaveStreamerMode();
}
