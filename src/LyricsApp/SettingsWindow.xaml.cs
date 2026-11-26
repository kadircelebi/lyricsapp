using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LyricsApp.Media;

namespace LyricsApp;

public partial class SettingsWindow : Window
{
    private readonly DisplaySettings display;

    /// <param name="players">The players reporting a song right now; the ignored ones come from the settings.</param>
    /// <param name="ignoredAppsChanged">Called after the "never follow" list changed, so it takes effect at once.</param>
    public SettingsWindow(AppSettings settings, IReadOnlyList<MediaPlayerInfo> players, Action ignoredAppsChanged)
    {
        InitializeComponent();
        display = settings.Display;
        DataContext = display;
        // Checked/Unchecked rather than Click, so a change made through UI Automation (screen readers) counts too.
        RecognizeBox.IsChecked = settings.RecognizeUnfoundSongs;
        RecognizeBox.Checked += (_, _) => settings.RecognizeUnfoundSongs = true;
        RecognizeBox.Unchecked += (_, _) => settings.RecognizeUnfoundSongs = false;
        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCulture).ToList();
        ShowSources(settings, players, ignoredAppsChanged);
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    /// <summary>One box per player: the running ones and the ones turned off earlier. Unticked means never followed.</summary>
    private void ShowSources(AppSettings settings, IReadOnlyList<MediaPlayerInfo> players, Action ignoredAppsChanged)
    {
        var apps = players.Select(p => new SavedApp(p.App, p.Name))
            .Concat(settings.IgnoredApps)
            .DistinctBy(app => app.Id)
            .OrderBy(app => app.Name, StringComparer.CurrentCulture)
            .ToList();
        SourcesNone.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var app in apps)
        {
            var box = new CheckBox
            {
                Content = new TextBlock { Text = app.Name, TextWrapping = TextWrapping.Wrap, Foreground = Foreground },
                IsChecked = settings.IgnoredApps.All(ignored => ignored.Id != app.Id),
            };
            box.Checked += (_, _) =>
            {
                settings.IgnoredApps.RemoveAll(ignored => ignored.Id == app.Id);
                ignoredAppsChanged();
            };
            box.Unchecked += (_, _) =>
            {
                if (settings.IgnoredApps.All(ignored => ignored.Id != app.Id)) settings.IgnoredApps.Add(app);
                ignoredAppsChanged();
            };
            SourcesList.Children.Add(box);
        }
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
