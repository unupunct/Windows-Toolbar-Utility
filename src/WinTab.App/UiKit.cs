using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinTab.App;

/// <summary>Colours and small control factories so the windows share one look.</summary>
internal static class UiKit
{
    public static readonly Brush Background = Brush("#1F2127");
    public static readonly Brush Card = Brush("#2A2D35");
    public static readonly Brush CardBorder = Brush("#3A3E49");
    public static readonly Brush Text = Brush("#EDEFF5");
    public static readonly Brush Muted = Brush("#9AA0AE");
    public static readonly Brush Accent = Brush("#4C8DFF");
    public static readonly Brush Warn = Brush("#FFB84C");

    public static Brush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    public static TextBlock Label(string text, double size = 12, Brush? foreground = null, FontWeight? weight = null)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = foreground ?? Text,
            FontWeight = weight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

    public static TextBlock Section(string text)
        => Label(text.ToUpperInvariant(), 10.5, Muted, FontWeights.SemiBold);

    public static Button PillButton(string text, RoutedEventHandler onClick, double minWidth = 0)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource("PillButton"),
            MinWidth = minWidth,
        };
        button.Click += onClick;
        return button;
    }

    public static Border CardPanel(UIElement child, double padding = 10)
        => new()
        {
            Background = Card,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(padding),
            Child = child,
        };

    public static Slider VolumeSlider()
        => new()
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            LargeChange = 5,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120,
        };

    public static ProgressBar Bar()
        => new()
        {
            Minimum = 0,
            Maximum = 100,
            Height = 4,
            Foreground = Accent,
            Background = Brush("#3A3E49"),
            BorderThickness = new Thickness(0),
        };

    public static bool IsDragging(Slider slider)
        => slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin;
}
