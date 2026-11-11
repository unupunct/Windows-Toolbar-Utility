using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinTab.Core;
using WinTab.Core.Cleaner;

namespace WinTab.App;

/// <summary>The Clean window: junk cleaner, app uninstaller and large-file finder.</summary>
public sealed class CleanWindow : Window
{
    private readonly ToggleButton _tabClean = Tab("Clean");
    private readonly ToggleButton _tabApps = Tab("Uninstall apps");
    private readonly ToggleButton _tabLarge = Tab("Large files");
    private readonly Grid _body = new();
    private readonly StackPanel _cleanPanel = new();
    private readonly StackPanel _appsPanel = new();
    private readonly StackPanel _largePanel = new();

    // Clean tab
    private IReadOnlyList<CleanCategory> _categories = Array.Empty<CleanCategory>();
    private readonly StackPanel _categoryRows = new();
    private ScrollViewer? _cleanScroll;
    private ScrollViewer? _appsScroll;
    private ScrollViewer? _largeScroll;
    private readonly TextBlock _cleanStatus = UiKit.Label("", 12, UiKit.Muted);
    private readonly ProgressBar _cleanProgress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed, Foreground = UiKit.Accent, Background = UiKit.CardBorder, BorderThickness = new Thickness(0) };
    private Button? _cleanButton;
    private Button? _scanButton;

    // Apps tab
    private IReadOnlyList<InstalledApp> _apps = Array.Empty<InstalledApp>();
    private readonly StackPanel _appRows = new();
    private readonly TextBox _appFilter = new();
    private readonly TextBlock _appStatus = UiKit.Label("", 12, UiKit.Muted);
    private bool _appsLoaded;

    // Large files tab
    private IReadOnlyList<LargeFile> _large = Array.Empty<LargeFile>();
    private readonly StackPanel _largeRows = new();
    private readonly TextBlock _largeStatus = UiKit.Label("", 12, UiKit.Muted);
    private readonly TextBlock _largeSelected = UiKit.Label("", 12, UiKit.Muted);
    private readonly ProgressBar _largeProgress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed, Foreground = UiKit.Accent, Background = UiKit.CardBorder, BorderThickness = new Thickness(0) };
    private Button? _trashButton;
    private long _largeMinimumBytes = 500L * 1024 * 1024;
    private readonly List<ToggleButton> _thresholds = new();

    public CleanWindow()
    {
        Title = "Unupunct WinTab: clean and free space";
        Width = 780;
        Height = 720;
        MinWidth = 640;
        MinHeight = 480;
        Background = UiKit.Background;
        Foreground = UiKit.Text;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var root = new DockPanel { Background = UiKit.Background };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 12, 14, 8) };
        tabs.Children.Add(_tabClean);
        tabs.Children.Add(_tabApps);
        tabs.Children.Add(_tabLarge);
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);

        BuildCleanPanel();
        BuildAppsPanel();
        BuildLargePanel();
        _body.Children.Add(_cleanPanel);
        _body.Children.Add(_appsPanel);
        _body.Children.Add(_largePanel);
        root.Children.Add(_body);
        Content = root;
        _body.SizeChanged += (_, _) => FitLists();

        _tabClean.Checked += (_, _) => ShowTab(0);
        _tabApps.Checked += (_, _) => ShowTab(1);
        _tabLarge.Checked += (_, _) => ShowTab(2);
        _tabClean.IsChecked = true;
    }

    /// <summary>Lets each list use the height the window has, leaving room for its header and button bar.</summary>
    private void FitLists()
    {
        double height = _body.ActualHeight;
        if (_cleanScroll != null) _cleanScroll.Height = Math.Max(150, height - 150);
        if (_appsScroll != null) _appsScroll.Height = Math.Max(150, height - 200);
        if (_largeScroll != null) _largeScroll.Height = Math.Max(150, height - 215);
    }

    private static ToggleButton Tab(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.FindResource("SegmentButton"),
        Margin = new Thickness(0, 0, 6, 0),
    };

    public void ShowTab(int index)
    {
        _cleanPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _appsPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _largePanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        _tabClean.IsChecked = index == 0;
        _tabApps.IsChecked = index == 1;
        _tabLarge.IsChecked = index == 2;
        if (index == 0 && _categories.Count == 0) _ = ScanCleanAsync();
        if (index == 1 && !_appsLoaded) _ = LoadAppsAsync();
    }

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Content = content,
        Margin = new Thickness(14, 4, 14, 4),
    };

    private static UIElement Bar(UIElement left, params UIElement[] right)
    {
        var dock = new DockPanel { Margin = new Thickness(14, 8, 14, 14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var element in right)
        {
            if (element is FrameworkElement fe) fe.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(element);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        dock.Children.Add(left);
        return dock;
    }

    private static StackPanel PanelShell(string title, string description, params UIElement[] extra)
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed };
        var header = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        header.Children.Add(UiKit.Label(title, 15, UiKit.Text, FontWeights.SemiBold));
        var text = UiKit.Label(description, 12, UiKit.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(0, 3, 0, 0);
        header.Children.Add(text);
        panel.Children.Add(header);
        foreach (var e in extra) panel.Children.Add(e);
        return panel;
    }

    // ------------------------------------------------------------------ Clean

    private void BuildCleanPanel()
    {
        _cleanPanel.Visibility = Visibility.Collapsed;
        var header = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        header.Children.Add(UiKit.Label("Clean up junk files", 15, UiKit.Text, FontWeights.SemiBold));
        var text = UiKit.Label("Review what was found, tick what you want gone, then clean. Documents, browser history, cookies and passwords are never touched.", 12, UiKit.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(0, 3, 0, 0);
        header.Children.Add(text);
        _cleanPanel.Children.Add(header);
        _cleanPanel.Children.Add(_cleanProgress);

        _cleanScroll = Scroll(_categoryRows);
        _cleanScroll.Height = 380;
        _cleanPanel.Children.Add(_cleanScroll);

        _scanButton = UiKit.PillButton("Scan again", (_, _) => _ = ScanCleanAsync());
        _cleanButton = UiKit.PillButton("Clean selected", (_, _) => _ = CleanAsync());
        _cleanPanel.Children.Add(Bar(_cleanStatus, _scanButton, _cleanButton));
    }

    private async Task ScanCleanAsync()
    {
        _cleanButton!.IsEnabled = false;
        _scanButton!.IsEnabled = false;
        _cleanProgress.Visibility = Visibility.Visible;
        _cleanStatus.Text = "Scanning...";
        try
        {
            IReadOnlyList<CleanCategory> categories;
            if (Demo.Enabled)
            {
                categories = Demo.Categories();
            }
            else
            {
                categories = JunkScanner.BuildCategories();
                await Task.Run(() => JunkScanner.Measure(categories));
            }
            _categories = categories;
            RenderCategories();
        }
        catch (Exception ex)
        {
            _cleanStatus.Text = "Scan failed: " + ex.Message;
        }
        finally
        {
            _cleanProgress.Visibility = Visibility.Collapsed;
            _scanButton.IsEnabled = true;
            UpdateCleanSummary();
        }
    }

    private void RenderCategories()
    {
        _categoryRows.Children.Clear();
        foreach (var category in _categories)
        {
            var box = new CheckBox { IsChecked = category.Selected, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var captured = category;
            box.Checked += (_, _) => { captured.Selected = true; UpdateCleanSummary(); };
            box.Unchecked += (_, _) => { captured.Selected = false; UpdateCleanSummary(); };
            var texts = new StackPanel();
            texts.Children.Add(UiKit.Label(category.Title, 13, UiKit.Text, FontWeights.SemiBold));
            var description = UiKit.Label(category.Description, 11.5, UiKit.Muted);
            description.TextWrapping = TextWrapping.Wrap;
            description.TextTrimming = TextTrimming.None;
            texts.Children.Add(description);
            var size = UiKit.Label(Fmt.Bytes(category.Size), 13, UiKit.Text, FontWeights.SemiBold);
            size.Margin = new Thickness(10, 0, 0, 0);

            var dock = new DockPanel();
            DockPanel.SetDock(box, Dock.Left);
            DockPanel.SetDock(size, Dock.Right);
            dock.Children.Add(box);
            dock.Children.Add(size);
            dock.Children.Add(texts);
            var card = UiKit.CardPanel(dock, 12);
            card.Margin = new Thickness(0, 0, 0, 8);
            _categoryRows.Children.Add(card);
        }
    }

    private void UpdateCleanSummary()
    {
        long selected = _categories.Where(c => c.Selected).Sum(c => c.Size);
        long total = _categories.Sum(c => c.Size);
        if (_categories.Count > 0)
            _cleanStatus.Text = $"{Fmt.Bytes(total)} found, {Fmt.Bytes(selected)} selected.";
        _cleanButton!.IsEnabled = selected > 0 && !Demo.Enabled;
    }

    private async Task CleanAsync()
    {
        var chosen = _categories.Where(c => c.Selected).ToList();
        if (chosen.Count == 0) return;
        string list = string.Join(Environment.NewLine, chosen.Select(c => $"  {c.Title}: {Fmt.Bytes(c.Size)}"));
        string extra = chosen.Any(c => c.IsRecycleBin) ? Environment.NewLine + Environment.NewLine + "The Recycle Bin will be emptied for good." : "";
        var answer = MessageBox.Show(this,
            "Delete these files?" + Environment.NewLine + Environment.NewLine + list + extra,
            "Clean", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        _cleanButton!.IsEnabled = false;
        _scanButton!.IsEnabled = false;
        _cleanProgress.Visibility = Visibility.Visible;
        _cleanStatus.Text = "Cleaning...";
        try
        {
            var result = await Task.Run(() => JunkScanner.Clean(chosen));
            _cleanStatus.Text = $"Freed {Fmt.Bytes(result.FreedBytes)} ({result.RemovedFiles} files)."
                + (result.FailedFiles > 0 ? $" {result.FailedFiles} were in use or protected and were skipped." : "");
        }
        catch (Exception ex)
        {
            _cleanStatus.Text = "Cleaning failed: " + ex.Message;
        }
        finally
        {
            _cleanProgress.Visibility = Visibility.Collapsed;
            _scanButton.IsEnabled = true;
        }
        string message = _cleanStatus.Text;
        await ScanCleanAsync();
        _cleanStatus.Text = message;
    }

    // ------------------------------------------------------------------ Uninstall apps

    private void BuildAppsPanel()
    {
        _appsPanel.Visibility = Visibility.Collapsed;
        var header = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        header.Children.Add(UiKit.Label("Uninstall apps", 15, UiKit.Text, FontWeights.SemiBold));
        var text = UiKit.Label("Lists the programs Windows can uninstall, biggest first when sorted by size. Uninstall starts the program's own uninstaller, which asks you to confirm.", 12, UiKit.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(0, 3, 0, 0);
        header.Children.Add(text);
        _appsPanel.Children.Add(header);

        _appFilter.Margin = new Thickness(14, 4, 14, 6);
        _appFilter.Padding = new Thickness(8, 6, 8, 6);
        _appFilter.Background = UiKit.Card;
        _appFilter.Foreground = UiKit.Text;
        _appFilter.CaretBrush = UiKit.Text;
        _appFilter.BorderBrush = UiKit.CardBorder;
        _appFilter.ToolTip = "Type to filter by name or publisher";
        _appFilter.TextChanged += (_, _) => RenderApps();
        _appsPanel.Children.Add(_appFilter);

        _appsScroll = Scroll(_appRows);
        _appsScroll.Height = 340;
        _appsPanel.Children.Add(_appsScroll);
        _appsPanel.Children.Add(Bar(_appStatus));
    }

    private async Task LoadAppsAsync()
    {
        _appsLoaded = true;
        _appStatus.Text = "Reading the list of installed programs...";
        _apps = Demo.Enabled ? Demo.Apps : await Task.Run(() => InstalledApps.List());
        RenderApps();
    }

    private void RenderApps()
    {
        string filter = _appFilter.Text.Trim();
        var shown = _apps.Where(a =>
            filter.Length == 0
            || a.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || a.Publisher.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(a => a.SizeBytes)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _appRows.Children.Clear();
        foreach (var app in shown) _appRows.Children.Add(AppRowFor(app));
        _appStatus.Text = $"{shown.Count} program(s).";
    }

    private UIElement AppRowFor(InstalledApp app)
    {
        var texts = new StackPanel();
        texts.Children.Add(UiKit.Label(app.Name, 13, UiKit.Text, FontWeights.SemiBold));
        string detail = string.Join("  |  ", new[] { app.Publisher, app.Version }.Where(s => !string.IsNullOrWhiteSpace(s)));
        texts.Children.Add(UiKit.Label(detail, 11.5, UiKit.Muted));
        var size = UiKit.Label(app.SizeBytes > 0 ? Fmt.Bytes(app.SizeBytes) : "", 12, UiKit.Muted);
        size.Margin = new Thickness(10, 0, 10, 0);
        size.MinWidth = 70;
        size.TextAlignment = TextAlignment.Right;
        var button = UiKit.PillButton("Uninstall", (_, _) => Uninstall(app));
        button.IsEnabled = !Demo.Enabled;

        var dock = new DockPanel();
        DockPanel.SetDock(button, Dock.Right);
        DockPanel.SetDock(size, Dock.Right);
        dock.Children.Add(button);
        dock.Children.Add(size);
        dock.Children.Add(texts);
        var card = UiKit.CardPanel(dock, 10);
        card.Margin = new Thickness(0, 0, 0, 6);
        return card;
    }

    private void Uninstall(InstalledApp app)
    {
        var answer = MessageBox.Show(this,
            $"Start the uninstaller for {app.Name}?",
            "Uninstall", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        if (!InstalledApps.StartUninstaller(app))
            MessageBox.Show(this, "The uninstaller could not be started.", "Uninstall", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ------------------------------------------------------------------ Large files

    private void BuildLargePanel()
    {
        _largePanel.Visibility = Visibility.Collapsed;
        var header = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        header.Children.Add(UiKit.Label("Large files", 15, UiKit.Text, FontWeights.SemiBold));
        var text = UiKit.Label("Looks through Downloads, Desktop, Documents, Videos, Music and Pictures. Nothing is removed until you tick a file and confirm, and removed files go to the Recycle Bin.", 12, UiKit.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(0, 3, 0, 0);
        header.Children.Add(text);
        _largePanel.Children.Add(header);

        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 4, 14, 6) };
        options.Children.Add(UiKit.Label("Bigger than", 12, UiKit.Muted));
        (string Label, long Bytes)[] steps =
        {
            ("100 MB", 100L * 1024 * 1024), ("500 MB", 500L * 1024 * 1024),
            ("1 GB", 1024L * 1024 * 1024), ("2 GB", 2048L * 1024 * 1024),
        };
        foreach (var step in steps)
        {
            var toggle = new ToggleButton
            {
                Content = step.Label,
                Style = (Style)Application.Current.FindResource("SegmentButton"),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
                IsChecked = step.Bytes == _largeMinimumBytes,
            };
            long bytes = step.Bytes;
            toggle.Click += (_, _) =>
            {
                _largeMinimumBytes = bytes;
                foreach (var other in _thresholds) other.IsChecked = ReferenceEquals(other, toggle);
            };
            _thresholds.Add(toggle);
            options.Children.Add(toggle);
        }
        var scan = UiKit.PillButton("Scan", (_, _) => _ = ScanLargeAsync());
        scan.Margin = new Thickness(14, 0, 0, 0);
        options.Children.Add(scan);
        _largePanel.Children.Add(options);
        _largePanel.Children.Add(_largeProgress);

        _largeScroll = Scroll(_largeRows);
        _largeScroll.Height = 300;
        _largePanel.Children.Add(_largeScroll);

        _trashButton = UiKit.PillButton("Move to Recycle Bin", (_, _) => TrashSelected());
        _trashButton.IsEnabled = false;
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_largeStatus);
        left.Children.Add(_largeSelected);
        _largeSelected.Margin = new Thickness(10, 0, 0, 0);
        _largePanel.Children.Add(Bar(left, _trashButton));
        _largeStatus.Text = "Press Scan to look for big files.";
    }

    internal async Task ScanLargeAsync()
    {
        _largeProgress.Visibility = Visibility.Visible;
        _largeStatus.Text = "Looking for big files...";
        _largeRows.Children.Clear();
        try
        {
            _large = Demo.Enabled
                ? Demo.LargeFiles
                : await Task.Run(() => LargeFilesScanner.Scan(LargeFilesScanner.DefaultRoots(), _largeMinimumBytes));
            RenderLarge();
            _largeStatus.Text = _large.Count == 0 ? "No files that big were found." : $"{_large.Count} file(s) found.";
        }
        catch (Exception ex)
        {
            _largeStatus.Text = "Scan failed: " + ex.Message;
        }
        finally
        {
            _largeProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderLarge()
    {
        _largeRows.Children.Clear();
        foreach (var file in _large)
        {
            var box = new CheckBox { IsChecked = file.Selected, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var captured = file;
            box.Checked += (_, _) => { captured.Selected = true; UpdateLargeSummary(); };
            box.Unchecked += (_, _) => { captured.Selected = false; UpdateLargeSummary(); };
            var texts = new StackPanel();
            texts.Children.Add(UiKit.Label(file.Name, 12.5, UiKit.Text, FontWeights.SemiBold));
            var folder = UiKit.Label(file.Folder, 11, UiKit.Muted);
            folder.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(folder);
            var size = UiKit.Label(Fmt.Bytes(file.Size), 12.5, UiKit.Text, FontWeights.SemiBold);
            size.Margin = new Thickness(10, 0, 10, 0);
            var show = UiKit.PillButton("Show", (_, _) => ShowInExplorer(captured.Path));
            show.Padding = new Thickness(8, 3, 8, 3);
            show.IsEnabled = !Demo.Enabled;

            var dock = new DockPanel();
            DockPanel.SetDock(box, Dock.Left);
            DockPanel.SetDock(show, Dock.Right);
            DockPanel.SetDock(size, Dock.Right);
            dock.Children.Add(box);
            dock.Children.Add(show);
            dock.Children.Add(size);
            dock.Children.Add(texts);
            var card = UiKit.CardPanel(dock, 8);
            card.Margin = new Thickness(0, 0, 0, 5);
            _largeRows.Children.Add(card);
        }
        UpdateLargeSummary();
    }

    private static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void UpdateLargeSummary()
    {
        var selected = _large.Where(f => f.Selected).ToList();
        _largeSelected.Text = selected.Count == 0 ? "" : $"{selected.Count} selected, {Fmt.Bytes(selected.Sum(f => f.Size))}";
        _trashButton!.IsEnabled = selected.Count > 0 && !Demo.Enabled;
    }

    private void TrashSelected()
    {
        var selected = _large.Where(f => f.Selected).ToList();
        if (selected.Count == 0) return;
        var answer = MessageBox.Show(this,
            $"Move {selected.Count} file(s), {Fmt.Bytes(selected.Sum(f => f.Size))}, to the Recycle Bin?",
            "Large files", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        long freed = 0;
        int failed = 0;
        foreach (var file in selected)
        {
            if (LargeFilesScanner.MoveToRecycleBin(file.Path)) freed += file.Size;
            else failed++;
        }
        _large = _large.Where(f => !f.Selected || !File.Exists(f.Path)).ToList();
        RenderLarge();
        _largeStatus.Text = $"Moved {Fmt.Bytes(freed)} to the Recycle Bin." + (failed > 0 ? $" {failed} could not be moved." : "");
    }

    private static class File
    {
        public static bool Exists(string path) => System.IO.File.Exists(path);
    }
}
