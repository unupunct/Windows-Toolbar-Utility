using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinTab.Core;
using WinTab.Core.Audio;
using WinTab.Core.Monitor;
using WinTab.Core.SendTo;
using Forms = System.Windows.Forms;

namespace WinTab.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private Forms.NotifyIcon? _tray;
    private AudioService? _audio;
    private SystemMonitor? _monitor;
    private SendToService? _sendTo;
    private AppSettings _settings = new();
    private FlyoutWindow? _flyout;
    private OverlayWindow? _overlay;
    private CleanWindow? _clean;
    private DispatcherTimer? _timer;
    private int _lastPercent = -1;
    private int _tick;
    private System.Drawing.Icon? _currentIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args = e.Args;
        string? screenshotDir = ArgValue(args, "--screenshots");
        Demo.Enabled = args.Contains("--demo") || screenshotDir != null;

        if (screenshotDir == null)
        {
            _mutex = new Mutex(true, "Local\\UnupunctWinTab.SingleInstance", out bool first);
            if (!first)
            {
                Shutdown();
                return;
            }
        }

        _settings = AppSettings.Load();
        _audio = new AudioService();
        _monitor = new SystemMonitor();
        _sendTo = new SendToService(screenshotDir != null
            ? Path.Combine(Path.GetTempPath(), "wintab-demo-" + Guid.NewGuid().ToString("N"))
            : null);

        _flyout = new FlyoutWindow(_audio, _sendTo, _settings, StartCapture, OpenClean, ApplySettings, Quit);
        _overlay = new OverlayWindow(() => _flyout?.ToggleFlyout());

        if (screenshotDir != null)
        {
            _ = TakeScreenshotsAsync(screenshotDir);
            return;
        }

        CreateTray();
        ApplySettings();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        OnTick();
    }

    private static string? ArgValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // ------------------------------------------------------------------ tray

    private void CreateTray()
    {
        _currentIcon = TrayIconRenderer.Render(0);
        _tray = new Forms.NotifyIcon
        {
            Icon = _currentIcon,
            Text = "Unupunct WinTab",
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _flyout?.ToggleFlyout();
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open panel", null, (_, _) => _flyout?.ShowFlyout());
        menu.Items.Add("Screenshot", null, (_, _) => StartCapture());
        menu.Items.Add("Clean...", null, (_, _) => OpenClean());
        menu.Items.Add(new Forms.ToolStripSeparator());
        var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = _settings.StartWithWindows, CheckOnClick = true };
        startup.CheckedChanged += (_, _) =>
        {
            _settings.StartWithWindows = startup.Checked;
            ApplySettings();
        };
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        _tray.ContextMenuStrip = menu;
    }

    private void ApplySettings()
    {
        _settings.Save();
        AppSettings.ApplyStartWithWindows(_settings.StartWithWindows, Environment.ProcessPath ?? "");
        if (_overlay == null) return;
        if (_settings.ShowTaskbarStrip)
        {
            if (!_overlay.IsVisible)
            {
                _overlay.Show();
            }
            _overlay.Reposition();
        }
        else if (_overlay.IsVisible)
        {
            _overlay.Hide();
        }
    }

    private void OnTick()
    {
        if (_monitor == null || _flyout == null) return;
        var snapshot = _monitor.Sample();
        int percent = (int)Math.Round(snapshot.CpuFraction * 100);
        if (percent != _lastPercent && _tray != null)
        {
            _lastPercent = percent;
            var icon = TrayIconRenderer.Render(percent);
            _tray.Icon = icon;
            _currentIcon?.Dispose();
            _currentIcon = icon;
        }
        string text = $"CPU {Fmt.Percent(snapshot.CpuFraction)}  RAM {Fmt.Percent(snapshot.MemoryFraction)}  "
            + $"DSK {Fmt.Percent(snapshot.SystemDriveUsedFraction)}  ↓{Fmt.RateCompact(snapshot.NetDownBytesPerSecond)} ↑{Fmt.RateCompact(snapshot.NetUpBytesPerSecond)}";
        if (_tray != null)
            _tray.Text = text.Length > 120 ? text[..120] : text;
        _overlay?.SetText(text);
        if (_tick++ % 5 == 0) _overlay?.Reposition();
        if (_flyout.IsVisible)
        {
            _flyout.Update(snapshot);
            _flyout.RefreshAudio();
        }
    }

    // ------------------------------------------------------------------ actions

    private void StartCapture()
    {
        // Give the panel a moment to disappear so it is not in the picture.
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            ScreenshotService.CaptureRegion(path =>
            {
                if (_tray != null)
                    _tray.ShowBalloonTip(2500, "Screenshot saved", "Copied to the clipboard and saved to " + path, Forms.ToolTipIcon.Info);
            });
        };
        delay.Start();
    }

    private void OpenClean()
    {
        if (_clean == null)
        {
            _clean = new CleanWindow();
            _clean.Closed += (_, _) => _clean = null;
        }
        _clean.Show();
        _clean.Activate();
    }

    private void Quit()
    {
        _timer?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _overlay?.Close();
        _flyout?.Close();
        _clean?.Close();
        _audio?.Dispose();
        _monitor?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }

    // ------------------------------------------------------------------ screenshots for the README

    private async Task TakeScreenshotsAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        try
        {
            _flyout!.Update(Demo.Snapshot);
            _flyout.ShowFlyout();
            await Task.Delay(700);
            _flyout.Update(Demo.Snapshot);
            await Task.Delay(300);
            SaveVisual((FrameworkElement)_flyout.Content, Path.Combine(dir, "panel.png"));
            _flyout.Hide();

            var clean = new CleanWindow();
            clean.Show();
            clean.ShowTab(0);
            await Task.Delay(1200);
            SaveWindow(clean, Path.Combine(dir, "clean.png"));
            clean.ShowTab(1);
            await Task.Delay(1000);
            SaveWindow(clean, Path.Combine(dir, "uninstall.png"));
            clean.ShowTab(2);
            await clean.ScanLargeAsync();
            await Task.Delay(500);
            SaveWindow(clean, Path.Combine(dir, "large-files.png"));
            clean.Close();
        }
        finally
        {
            Shutdown();
        }
    }

    private static void SaveWindow(Window window, string path)
        => SaveVisual((FrameworkElement)window.Content, path);

    private static void SaveVisual(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        double width = element.ActualWidth, height = element.ActualHeight;
        const double dpi = 192;
        var bitmap = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
