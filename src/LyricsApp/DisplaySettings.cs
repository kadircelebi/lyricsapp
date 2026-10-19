using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace LyricsApp;

/// <summary>Look-and-feel options. Bound live by the UI and saved inside settings.json.</summary>
public sealed class DisplaySettings : ObservableObject
{
    private bool topmost;
    private bool lyricsOnly;
    private double backgroundOpacity = 1.0;
    private bool focusMode = true;
    private int contextLines = 2;
    private bool lineFill;
    private string fontFamily = "Segoe UI Variable Display";
    private double fontSize = 26;
    private string activeColor = "#FFFFFFFF";
    private string inactiveColor = "#FF8E8E9C";
    private bool shadowEnabled = true;
    private string shadowColor = "#FF000000";
    private double shadowBlur = 8;
    private double shadowDepth = 1;
    private double shadowOpacity = 0.9;

    /// <summary>Keep the window above all others.</summary>
    public bool Topmost { get => topmost; set => Set(ref topmost, value); }

    /// <summary>Transparent window with no header: only the lyrics float on the desktop. Controls appear on hover.</summary>
    public bool LyricsOnly { get => lyricsOnly; set => Set(ref lyricsOnly, value); }

    /// <summary>Background opacity in normal mode (0 = fully transparent, 1 = solid).</summary>
    public double BackgroundOpacity { get => backgroundOpacity; set => Set(ref backgroundOpacity, Math.Clamp(value, 0, 1)); }

    /// <summary>Karaoke focus: show only the active line and <see cref="ContextLines"/> lines around it.</summary>
    public bool FocusMode { get => focusMode; set => Set(ref focusMode, value); }

    /// <summary>Lines shown before and after the active line in focus mode.</summary>
    public int ContextLines { get => contextLines; set => Set(ref contextLines, Math.Clamp(value, 0, 6)); }

    /// <summary>Sweep the active line from left to right as it is sung (redraws every frame while playing).</summary>
    public bool LineFill { get => lineFill; set => Set(ref lineFill, value); }

    public string FontFamily
    {
        get => fontFamily;
        set
        {
            if (Set(ref fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value))
                OnPropertyChanged(nameof(FontFamilyValue));
        }
    }

    /// <summary>Font size of the active line; other lines are drawn smaller.</summary>
    public double FontSize
    {
        get => fontSize;
        set
        {
            if (Set(ref fontSize, Math.Clamp(value, 12, 72)))
                OnPropertyChanged(nameof(InactiveFontSize));
        }
    }

    public string ActiveColor
    {
        get => activeColor;
        set { if (Set(ref activeColor, value)) OnPropertyChanged(nameof(ActiveBrush)); }
    }

    public string InactiveColor
    {
        get => inactiveColor;
        set { if (Set(ref inactiveColor, value)) OnPropertyChanged(nameof(InactiveBrush)); }
    }

    public bool ShadowEnabled { get => shadowEnabled; set => Set(ref shadowEnabled, value); }

    public string ShadowColor
    {
        get => shadowColor;
        set { if (Set(ref shadowColor, value)) OnPropertyChanged(nameof(ShadowColorValue)); }
    }

    public double ShadowBlur { get => shadowBlur; set => Set(ref shadowBlur, Math.Clamp(value, 0, 40)); }
    public double ShadowDepth { get => shadowDepth; set => Set(ref shadowDepth, Math.Clamp(value, 0, 12)); }
    public double ShadowOpacity { get => shadowOpacity; set => Set(ref shadowOpacity, Math.Clamp(value, 0, 1)); }

    [JsonIgnore] public double InactiveFontSize => Math.Round(FontSize * 0.75, 1);
    [JsonIgnore] public FontFamily FontFamilyValue => new(FontFamily);
    [JsonIgnore] public Brush ActiveBrush => ToBrush(ActiveColor, Colors.White);
    [JsonIgnore] public Brush InactiveBrush => ToBrush(InactiveColor, Colors.Gray);
    [JsonIgnore] public Color ShadowColorValue => ParseColor(ShadowColor) ?? Colors.Black;

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Brush ToBrush(string hex, Color fallback)
    {
        var brush = new SolidColorBrush(ParseColor(hex) ?? fallback);
        brush.Freeze();
        return brush;
    }
}
