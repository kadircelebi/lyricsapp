using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace LyricsApp;

/// <summary>Colour picker: a row of swatches plus a hex box. <see cref="Value"/> is "#AARRGGBB".</summary>
public partial class ColorField : UserControl
{
    private static readonly string[] DefaultSwatches =
    [
        "#FFFFFFFF", "#FFFFF59D", "#FFFFD54F", "#FFFFAB40", "#FFFF80AB", "#FFEF5350",
        "#FFB39DFF", "#FF64B5F6", "#FF4DD0E1", "#FF69F0AE", "#FF9E9E9E", "#FF5C5C66", "#FF000000",
    ];

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata("#FFFFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public ColorField()
    {
        InitializeComponent();
        Swatches.ItemsSource = DefaultSwatches;
        ShowValue(Value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorField)d).ShowValue((string)e.NewValue);

    private void ShowValue(string hex)
    {
        if (DisplaySettings.ParseColor(hex) is { } color) Preview.Color = color;
        if (!HexBox.IsKeyboardFocused) HexBox.Text = hex;
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex }) Value = hex;
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnHexCommitted(sender, e);
    }

    private void OnHexCommitted(object sender, RoutedEventArgs e)
    {
        if (DisplaySettings.ParseColor(HexBox.Text) is { } color) Value = DisplaySettings.ToHex(color);
        else HexBox.Text = Value;
    }
}
