using System.Windows;
using System.Windows.Media.Animation;

namespace LyricsApp;

/// <summary>Attached properties that make bound values change smoothly.</summary>
public static class Animate
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(250));

    /// <summary>Like Opacity, but animates to each new value instead of jumping.</summary>
    public static readonly DependencyProperty OpacityProperty = DependencyProperty.RegisterAttached(
        "Opacity", typeof(double), typeof(Animate), new PropertyMetadata(1.0, OnOpacityChanged));

    public static double GetOpacity(DependencyObject d) => (double)d.GetValue(OpacityProperty);

    public static void SetOpacity(DependencyObject d, double value) => d.SetValue(OpacityProperty, value);

    private static void OnOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if (!element.IsVisible)
        {
            // Lines coming back into view start transparent and fade in.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 0;
        }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation((double)e.NewValue, FadeDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
