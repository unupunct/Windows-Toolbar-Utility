using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinTab.Core;
using WinTab.Core.Audio;
using WinTab.Core.Monitor;
using WinTab.Core.SendTo;

namespace WinTab.App;

/// <summary>The panel that opens from the tray icon: volume, system readings and the three action buttons.</summary>
public sealed class FlyoutWindow : Window
{
    private readonly AudioService _audio;
    private readonly SendToService _sendTo;
    private readonly AppSettings _settings;
    private readonly Action _capture;
    private readonly Action _openClean;
    private readonly Action _settingsChanged;
    private readonly Action _quit;

    private readonly Slider _outSlider = UiKit.VolumeSlider();
    private readonly Slider _micSlider = UiKit.VolumeSlider();
    private readonly TextBlock _outPercent = UiKit.Label("", 11, UiKit.Muted);
    private readonly TextBlock _micPercent = UiKit.Label("", 11, UiKit.Muted);
    private readonly TextBlock _outName = UiKit.Label("Output", 11.5, UiKit.Muted);
    private readonly TextBlock _micName = UiKit.Label("Microphone", 11.5, UiKit.Muted);
    private readonly Button _outMute = IconButton("");
    private readonly Button _micMute = IconButton("");
    private readonly StackPanel _apps = new();
    private readonly TextBlock _appsNote = UiKit.Label("", 11, UiKit.Muted);
    private readonly TextBlock _status = UiKit.Label("", 11, UiKit.Muted);

    private readonly TextBlock _cpuValue = Big();
    private readonly TextBlock _cpuSub = UiKit.Label("", 10.5, UiKit.Muted);
    private readonly ProgressBar _cpuBar = UiKit.Bar();
    private readonly TextBlock _memValue = Big();
    private readonly TextBlock _memSub = UiKit.Label("", 10.5, UiKit.Muted);
    private readonly ProgressBar _memBar = UiKit.Bar();
    private readonly TextBlock _diskValue = Big();
    private readonly TextBlock _diskSub = UiKit.Label("", 10.5, UiKit.Muted);
    private readonly ProgressBar _diskBar = UiKit.Bar();
    private readonly TextBlock _netDown = Big();
    private readonly TextBlock _netUp = UiKit.Label("", 11.5, UiKit.Text, FontWeights.SemiBold);

    private bool _suppress;
    private string _appKey = "";
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>Set while a dialog or menu from this window is open, so losing focus does not close the panel.</summary>
    public bool KeepOpen { get; private set; }

    public FlyoutWindow(AudioService audio, SendToService sendTo, AppSettings settings,
        Action capture, Action openClean, Action settingsChanged, Action quit)
    {
        _audio = audio;
        _sendTo = sendTo;
        _settings = settings;
        _capture = capture;
        _openClean = openClean;
        _settingsChanged = settingsChanged;
        _quit = quit;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 384;
        Title = "Unupunct WinTab";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        Content = BuildContent();
        Deactivated += (_, _) =>
        {
            if (!KeepOpen)
            {
                _hiddenAt = Environment.TickCount64;
                Hide();
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
        _statusTimer.Tick += (_, _) =>
        {
            _status.Text = "";
            _statusTimer.Stop();
        };
        SizeChanged += (_, _) => PlaceNearTray();
    }

    private static Button IconButton(string glyph)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 14,
            },
            Style = (Style)Application.Current.FindResource("PillButton"),
            Padding = new Thickness(8, 5, 8, 5),
            Width = 34,
        };
        return button;
    }

    private static TextBlock Big() => UiKit.Label("", 17, UiKit.Text, FontWeights.SemiBold);

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(14) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var settingsButton = UiKit.PillButton("Settings", OnSettingsClick);
        settingsButton.Padding = new Thickness(10, 4, 10, 4);
        DockPanel.SetDock(settingsButton, Dock.Right);
        header.Children.Add(settingsButton);
        header.Children.Add(UiKit.Label("Unupunct WinTab", 14, UiKit.Text, FontWeights.SemiBold));
        root.Children.Add(header);

        // Volume
        root.Children.Add(UiKit.Section("Volume"));
        var volume = new StackPanel();
        volume.Children.Add(VolumeRow(_outMute, _outName, _outSlider, _outPercent));
        volume.Children.Add(VolumeRow(_micMute, _micName, _micSlider, _micPercent));
        volume.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = UiKit.CardBorder });
        volume.Children.Add(UiKit.Label("App volume", 11.5, UiKit.Muted, FontWeights.SemiBold));
        volume.Children.Add(_appsNote);
        volume.Children.Add(_apps);
        var volumeCard = UiKit.CardPanel(volume);
        volumeCard.Margin = new Thickness(0, 6, 0, 12);
        root.Children.Add(volumeCard);

        _outSlider.ValueChanged += (_, e) =>
        {
            if (_suppress) return;
            _audio.SetOutputVolume((float)(e.NewValue / 100.0));
            _outPercent.Text = $"{e.NewValue:0}%";
        };
        _micSlider.ValueChanged += (_, e) =>
        {
            if (_suppress) return;
            _audio.SetInputVolume((float)(e.NewValue / 100.0));
            _micPercent.Text = $"{e.NewValue:0}%";
        };
        _outMute.Click += (_, _) =>
        {
            _audio.SetOutputMuted(!(_audio.GetOutputMuted() ?? false));
            RefreshAudio();
        };
        _micMute.Click += (_, _) =>
        {
            _audio.SetInputMuted(!(_audio.GetInputMuted() ?? false));
            RefreshAudio();
        };

        // System
        root.Children.Add(UiKit.Section("System"));
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 12) };
        grid.Children.Add(Tile("CPU", _cpuValue, _cpuBar, _cpuSub));
        grid.Children.Add(Tile("Memory", _memValue, _memBar, _memSub));
        grid.Children.Add(Tile("Disk", _diskValue, _diskBar, _diskSub));
        var net = new StackPanel();
        net.Children.Add(UiKit.Label("Network", 11, UiKit.Muted));
        net.Children.Add(Row("↓", _netDown));
        net.Children.Add(Row("↑", _netUp));
        var netCard = UiKit.CardPanel(net);
        netCard.Margin = new Thickness(4, 4, 0, 0);
        grid.Children.Add(netCard);
        root.Children.Add(grid);

        // Buttons
        var buttons = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        var shot = UiKit.PillButton("Screenshot", (_, _) => { Hide(); _capture(); });
        var send = UiKit.PillButton("Send to", OnSendToClick);
        var clean = UiKit.PillButton("Clean", (_, _) => { Hide(); _openClean(); });
        foreach (var button in new[] { shot, send, clean })
        {
            button.Margin = new Thickness(2);
            buttons.Children.Add(button);
        }
        root.Children.Add(buttons);

        var footer = new DockPanel();
        var quit = UiKit.PillButton("Quit", (_, _) => _quit());
        quit.Padding = new Thickness(10, 4, 10, 4);
        DockPanel.SetDock(quit, Dock.Right);
        footer.Children.Add(quit);
        footer.Children.Add(_status);
        root.Children.Add(footer);

        return new Border
        {
            Background = UiKit.Background,
            BorderBrush = UiKit.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = root,
        };
    }

    private static UIElement Row(string glyph, TextBlock value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(UiKit.Label(glyph, 13, UiKit.Muted));
        value.Margin = new Thickness(6, 0, 0, 0);
        value.FontSize = 13;
        panel.Children.Add(value);
        return panel;
    }

    private static UIElement Tile(string title, TextBlock value, ProgressBar bar, TextBlock sub)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Label(title, 11, UiKit.Muted));
        stack.Children.Add(value);
        bar.Margin = new Thickness(0, 4, 0, 4);
        stack.Children.Add(bar);
        stack.Children.Add(sub);
        var card = UiKit.CardPanel(stack);
        card.Margin = new Thickness(0, 0, 4, 4);
        return card;
    }

    private static UIElement VolumeRow(Button mute, TextBlock name, Slider slider, TextBlock percent)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(slider, 2);
        Grid.SetColumn(percent, 3);
        percent.TextAlignment = TextAlignment.Right;
        name.Margin = new Thickness(8, 0, 6, 0);
        grid.Children.Add(mute);
        grid.Children.Add(name);
        grid.Children.Add(slider);
        grid.Children.Add(percent);
        return grid;
    }

    // ------------------------------------------------------------------ updates

    public void ShowFlyout()
    {
        RefreshAudio();
        Show();
        PlaceNearTray();
        Activate();
    }

    private long _hiddenAt;

    public void ToggleFlyout()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }
        // Clicking the tray icon while the panel is open first takes focus away
        // (closing it); do not reopen it from that very same click.
        if (Environment.TickCount64 - _hiddenAt < 300) return;
        ShowFlyout();
    }

    private void PlaceNearTray()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 12;
        Top = Math.Max(work.Top + 8, work.Bottom - ActualHeight - 12);
    }

    public void Update(SystemSnapshot s)
    {
        _cpuValue.Text = Fmt.Percent(s.CpuFraction);
        _cpuBar.Value = s.CpuFraction * 100;
        _cpuSub.Text = "Processor load";
        _memValue.Text = Fmt.Percent(s.MemoryFraction);
        _memBar.Value = s.MemoryFraction * 100;
        _memSub.Text = $"{Fmt.Bytes(s.MemoryUsedBytes)} of {Fmt.Bytes(s.MemoryTotalBytes)}";
        _diskValue.Text = Fmt.Percent(s.SystemDriveUsedFraction);
        _diskBar.Value = s.SystemDriveUsedFraction * 100;
        _diskSub.Text = $"{s.SystemDriveName} {Fmt.Bytes(s.SystemDriveFreeBytes)} free, busy {Fmt.Percent(s.DiskActivityFraction)}";
        _netDown.Text = Fmt.Rate(s.NetDownBytesPerSecond);
        _netUp.Text = Fmt.Rate(s.NetUpBytesPerSecond);
    }

    public void RefreshAudio()
    {
        _suppress = true;
        try
        {
            _outName.Text = Short(_audio.OutputDeviceName, "Output");
            _micName.Text = Short(_audio.InputDeviceName, "Microphone");
            var output = _audio.GetOutputVolume();
            var input = _audio.GetInputVolume();
            _outSlider.IsEnabled = output != null;
            _micSlider.IsEnabled = input != null;
            if (output != null && !UiKit.IsDragging(_outSlider)) _outSlider.Value = output.Value * 100;
            if (input != null && !UiKit.IsDragging(_micSlider)) _micSlider.Value = input.Value * 100;
            _outPercent.Text = output == null ? "-" : $"{output.Value * 100:0}%";
            _micPercent.Text = input == null ? "-" : $"{input.Value * 100:0}%";
            SetGlyph(_outMute, _audio.GetOutputMuted() == true ? "" : "");
            SetGlyph(_micMute, _audio.GetInputMuted() == true ? "" : "");
            RefreshApps();
        }
        finally
        {
            _suppress = false;
        }
    }

    private static string Short(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        int paren = name.IndexOf('(');
        string trimmed = paren > 0 ? name[..paren].Trim() : name;
        return trimmed.Length == 0 ? fallback : trimmed;
    }

    private static void SetGlyph(Button button, string glyph)
    {
        if (button.Content is TextBlock block) block.Text = glyph;
    }

    private void RefreshApps()
    {
        IReadOnlyList<AppAudioSession> sessions = Demo.Enabled ? Demo.Sessions : _audio.GetAppSessions();
        string key = string.Join(",", sessions.Select(s => s.ProcessId));
        _appsNote.Text = sessions.Count == 0 ? "No app is using sound right now." : "";
        if (key != _appKey)
        {
            _appKey = key;
            _apps.Children.Clear();
            foreach (var session in sessions) _apps.Children.Add(AppRow(session));
            return;
        }
        for (int i = 0; i < sessions.Count && i < _apps.Children.Count; i++)
        {
            if (_apps.Children[i] is Grid row && row.Tag is AppRowParts parts && !UiKit.IsDragging(parts.Slider))
            {
                parts.Slider.Value = sessions[i].Volume * 100;
                parts.Percent.Text = $"{sessions[i].Volume * 100:0}%";
                SetGlyph(parts.Mute, sessions[i].IsMuted ? "" : "");
            }
        }
    }

    private sealed record AppRowParts(Slider Slider, TextBlock Percent, Button Mute);

    private UIElement AppRow(AppAudioSession session)
    {
        var slider = UiKit.VolumeSlider();
        slider.Value = session.Volume * 100;
        var percent = UiKit.Label($"{session.Volume * 100:0}%", 11, UiKit.Muted);
        var mute = IconButton(session.IsMuted ? "" : "");
        mute.Width = 28;
        mute.Padding = new Thickness(4, 3, 4, 3);
        var name = UiKit.Label(session.Name, 11.5, session.IsActive ? UiKit.Text : UiKit.Muted);
        name.ToolTip = session.IsActive ? session.Name : session.Name + " (not playing)";
        name.Margin = new Thickness(8, 0, 6, 0);
        percent.TextAlignment = TextAlignment.Right;

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0), Tag = new AppRowParts(slider, percent, mute) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(slider, 2);
        Grid.SetColumn(percent, 3);
        grid.Children.Add(mute);
        grid.Children.Add(name);
        grid.Children.Add(slider);
        grid.Children.Add(percent);

        slider.ValueChanged += (_, e) =>
        {
            if (_suppress) return;
            percent.Text = $"{e.NewValue:0}%";
            if (!Demo.Enabled) _audio.SetAppVolume(session.ProcessId, (float)(e.NewValue / 100.0));
        };
        bool muted = session.IsMuted;
        mute.Click += (_, _) =>
        {
            muted = !muted;
            SetGlyph(mute, muted ? "" : "");
            if (!Demo.Enabled) _audio.SetAppMuted(session.ProcessId, muted);
        };
        return grid;
    }

    // ------------------------------------------------------------------ menus

    private void SetStatus(string text)
    {
        _status.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var strip = new MenuItem { Header = "Show readings next to the tray", IsCheckable = true, IsChecked = _settings.ShowTaskbarStrip };
        strip.Click += (_, _) =>
        {
            _settings.ShowTaskbarStrip = strip.IsChecked;
            _settings.Save();
            _settingsChanged();
        };
        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = _settings.StartWithWindows };
        startup.Click += (_, _) =>
        {
            _settings.StartWithWindows = startup.IsChecked;
            _settings.Save();
            _settingsChanged();
        };
        menu.Items.Add(strip);
        menu.Items.Add(startup);
        OpenMenu(menu);
    }

    private void OnSendToClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Top };
        foreach (var destination in _sendTo.Destinations.ToList())
        {
            var item = new MenuItem { Header = destination.Name + (destination.IsApp ? "  (app)" : "") };
            var captured = destination;
            item.Click += (_, _) => ChooseAndSend(captured);
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var addFolder = new MenuItem { Header = "Add a folder, drive or network location..." };
        addFolder.Click += (_, _) => AddFolder();
        var addApp = new MenuItem { Header = "Add an app..." };
        addApp.Click += (_, _) => AddApp();
        menu.Items.Add(addFolder);
        menu.Items.Add(addApp);
        if (_sendTo.Destinations.Count > 0)
        {
            var remove = new MenuItem { Header = "Remove" };
            foreach (var destination in _sendTo.Destinations.ToList())
            {
                var item = new MenuItem { Header = destination.Name };
                var captured = destination;
                item.Click += (_, _) => _sendTo.Remove(captured);
                remove.Items.Add(item);
            }
            menu.Items.Add(remove);
        }
        OpenMenu(menu);
    }

    private void OpenMenu(ContextMenu menu)
    {
        KeepOpen = true;
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(new Action(() => KeepOpen = false), DispatcherPriority.Background);
        menu.IsOpen = true;
    }

    private void ChooseAndSend(SendToDestination destination)
    {
        KeepOpen = true;
        try
        {
            var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose what to send to " + destination.Name };
            if (dialog.ShowDialog(this) != true) return;
            var result = _sendTo.Send(dialog.FileNames, destination);
            if (result.Failures.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, result.Failures), "Send to", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                SetStatus($"Sent {result.Sent} item(s) to {destination.Name}.");
        }
        finally
        {
            KeepOpen = false;
        }
    }

    private void AddFolder()
    {
        KeepOpen = true;
        try
        {
            var dialog = new OpenFolderDialog { Title = "Choose a folder, drive or network location" };
            if (dialog.ShowDialog(this) != true) return;
            string path = dialog.FolderName;
            string name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
            _sendTo.Add(string.IsNullOrEmpty(name) ? path : name, path, isApp: false);
        }
        finally
        {
            KeepOpen = false;
        }
    }

    private void AddApp()
    {
        KeepOpen = true;
        try
        {
            var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose an app" };
            if (dialog.ShowDialog(this) != true) return;
            _sendTo.Add(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName, isApp: true);
        }
        finally
        {
            KeepOpen = false;
        }
    }
}
