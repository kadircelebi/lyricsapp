using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LyricsApp.Media;

namespace LyricsApp;

/// <summary>
/// The buttons of streamer mode, in a small window of their own on top of the main window, which lets every click
/// through in that mode: previous, play/pause and next for the player (when switched on), move and leave.
/// Being a separate window keeps them out of a capture too.
/// </summary>
public partial class StreamerControls : Window
{
    private readonly MainWindow mainWindow;
    private readonly MainViewModel viewModel;
    private NativeMethods.Point dragStartCursor;
    private Point dragStartWindow;

    public StreamerControls(MainWindow mainWindow, MainViewModel viewModel)
    {
        InitializeComponent();
        this.mainWindow = mainWindow;
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Display.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DisplaySettings.StreamerMediaButtons)) ApplyMediaButtons();
        };
        ApplyMediaButtons();
        SourceInitialized += (_, _) =>
        {
            // Clicking a button must not take the keyboard away from the game or the program being streamed.
            var handle = new WindowInteropHelper(this).Handle;
            var styles = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
                new IntPtr(styles | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
        };
    }

    /// <summary>Width of the window: move and leave, plus the three player buttons and their divider when shown.</summary>
    public static double WidthFor(bool mediaButtons) => mediaButtons ? 121 : 48;

    private void ApplyMediaButtons()
    {
        var show = viewModel.Display.StreamerMediaButtons;
        MediaButtons.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Width = WidthFor(show);
    }

    /// <summary>
    /// The corner of the window nearest to the middle of the screen it is on, so the buttons stay within reach
    /// when the window sits against a screen edge.
    /// </summary>
    public static (bool AtLeft, bool AtTop) ChooseCorner(Rect window, Rect workArea)
    {
        var windowIsRight = window.Left + window.Width / 2 > workArea.Left + workArea.Width / 2;
        var windowIsLow = window.Top + window.Height / 2 > workArea.Top + workArea.Height / 2;
        return (windowIsRight, windowIsLow);
    }

    // The main window is moved here, step by step with the pointer, so it is seen moving while it is dragged
    // (the system's own window drag only showed the result on a click-through window).
    // Preview events: a button that holds the mouse marks the plain MouseMove as handled.

    private void OnMoveMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!NativeMethods.GetCursorPos(out dragStartCursor)) return;
        // Before taking the mouse: capturing it raises a mouse move at once, which already needs the start position.
        dragStartWindow = new Point(mainWindow.Left, mainWindow.Top);
        if (MoveButton.CaptureMouse()) mainWindow.OnControlsDragStarted();
    }

    private void OnMoveMouseMove(object sender, MouseEventArgs e)
    {
        if (!MoveButton.IsMouseCaptured || !NativeMethods.GetCursorPos(out var cursor)) return;
        var dpi = VisualTreeHelper.GetDpi(mainWindow);
        mainWindow.Left = dragStartWindow.X + (cursor.X - dragStartCursor.X) / dpi.DpiScaleX;
        mainWindow.Top = dragStartWindow.Y + (cursor.Y - dragStartCursor.Y) / dpi.DpiScaleY;
    }

    private void OnMoveMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!MoveButton.IsMouseCaptured) return;
        e.Handled = true;
        MoveButton.ReleaseMouseCapture();
    }

    private void OnMoveLostCapture(object sender, MouseEventArgs e) => mainWindow.OnControlsDragEnded();

    private void OnExitClick(object sender, RoutedEventArgs e) => mainWindow.LeaveStreamerMode();

    private void OnPreviousClick(object sender, RoutedEventArgs e) => viewModel.SendMediaCommand(MediaCommand.Previous);

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => viewModel.SendMediaCommand(MediaCommand.PlayPause);

    private void OnNextClick(object sender, RoutedEventArgs e) => viewModel.SendMediaCommand(MediaCommand.Next);
}
