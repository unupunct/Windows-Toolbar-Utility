using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinTab.App;

/// <summary>
/// A small always-on-top strip with the CPU, memory, disk and network readings,
/// placed on the taskbar just left of the tray icons. Clicking it opens the panel.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly TextBlock _text;
    private readonly Action _onClick;

    public OverlayWindow(Action onClick)
    {
        _onClick = onClick;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false;
        Focusable = false;
        Title = "Unupunct WinTab readings";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Border
        {
            Background = UiKit.Brush("#CC1F2127"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 3, 9, 3),
            Cursor = Cursors.Hand,
            Child = _text,
        };
        MouseLeftButtonUp += (_, _) => _onClick();
        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int style = Native.GetWindowLong(hwnd, Native.GwlExStyle);
            Native.SetWindowLong(hwnd, Native.GwlExStyle, style | Native.WsExToolWindow | Native.WsExNoActivate);
        };
        SizeChanged += (_, _) => Reposition();
    }

    public void SetText(string text)
    {
        if (_text.Text != text) _text.Text = text;
    }

    /// <summary>Puts the strip left of the tray icons and keeps it above the taskbar.</summary>
    public void Reposition()
    {
        if (!IsVisible) return;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);
        if (!Native.TryGetTrayArea(out var tray) || !Native.TryGetTaskbar(out var bar)) return;
        int x = tray.Left - width - (int)(8 * scale);
        int y = bar.Top + (bar.Height - height) / 2;
        Native.SetWindowPos(hwnd, Native.HwndTopmost, x, y, width, height, Native.SwpNoActivate | Native.SwpShowWindow);
    }
}
