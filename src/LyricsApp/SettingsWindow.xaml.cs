using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LyricsApp;

public partial class SettingsWindow : Window
{
    private readonly DisplaySettings display;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        display = settings.Display;
        DataContext = display;
        RecognizeBox.IsChecked = settings.RecognizeUnfoundSongs;
        RecognizeBox.Click += (_, _) => settings.RecognizeUnfoundSongs = RecognizeBox.IsChecked == true;
        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCulture).ToList();
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "classic":
                Apply("#FFFFFFFF", "#FF8E8E9C", shadow: true, "#FF000000", blur: 8, depth: 1, opacity: 0.9);
                break;
            case "karaoke":
                Apply("#FFFFEB3B", "#FFFFFFFF", shadow: true, "#FF000000", blur: 6, depth: 0, opacity: 1.0);
                break;
            case "neon":
                Apply("#FF4DD0E1", "#FFB39DFF", shadow: true, "#FF00E5FF", blur: 18, depth: 0, opacity: 0.85);
                break;
            case "plain":
                Apply("#FFF2F2F5", "#FF6E6E7A", shadow: false, "#FF000000", blur: 0, depth: 0, opacity: 0);
                break;
        }
    }

    private void Apply(string active, string inactive, bool shadow, string shadowColor, double blur, double depth, double opacity)
    {
        display.ActiveColor = active;
        display.InactiveColor = inactive;
        display.ShadowEnabled = shadow;
        display.ShadowColor = shadowColor;
        display.ShadowBlur = blur;
        display.ShadowDepth = depth;
        display.ShadowOpacity = opacity;
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
