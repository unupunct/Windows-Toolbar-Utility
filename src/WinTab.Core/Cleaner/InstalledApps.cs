using Microsoft.Win32;

namespace WinTab.Core.Cleaner;

public sealed class InstalledApp
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public long SizeBytes { get; init; }
    public string InstallLocation { get; init; } = "";
    public string UninstallCommand { get; init; } = "";
    public string QuietUninstallCommand { get; init; } = "";
    public string DisplayIconPath { get; init; } = "";
}

/// <summary>
/// Lists the programs Windows knows how to uninstall, from the registry's
/// Uninstall keys, and starts the program's own uninstaller. It does not
/// delete anything itself.
/// </summary>
public static class InstalledApps
{
    private static readonly (RegistryHive Hive, RegistryView View, string Path)[] Sources =
    {
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    public static IReadOnlyList<InstalledApp> List()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, path) in Sources)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(path);
                if (key == null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    var app = Read(sub);
                    if (app != null) apps.TryAdd(app.Name + "|" + app.Version, app);
                }
            }
            catch
            {
            }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static InstalledApp? Read(RegistryKey? key)
    {
        if (key == null) return null;
        string? name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;
        // Hidden system components and update entries are not programs the user installed.
        if (key.GetValue("SystemComponent") is int system && system == 1) return null;
        if (key.GetValue("ParentKeyName") != null) return null;
        string uninstall = key.GetValue("UninstallString") as string ?? "";
        string quiet = key.GetValue("QuietUninstallString") as string ?? "";
        if (string.IsNullOrWhiteSpace(uninstall) && string.IsNullOrWhiteSpace(quiet)) return null;
        long sizeKb = key.GetValue("EstimatedSize") is int kb ? kb : 0;
        return new InstalledApp
        {
            Name = name.Trim(),
            Publisher = key.GetValue("Publisher") as string ?? "",
            Version = key.GetValue("DisplayVersion") as string ?? "",
            SizeBytes = sizeKb * 1024,
            InstallLocation = key.GetValue("InstallLocation") as string ?? "",
            UninstallCommand = uninstall,
            QuietUninstallCommand = quiet,
            DisplayIconPath = key.GetValue("DisplayIcon") as string ?? "",
        };
    }

    /// <summary>Starts the program's own uninstaller (it shows its own confirmation).</summary>
    public static bool StartUninstaller(InstalledApp app)
    {
        string command = app.UninstallCommand;
        if (string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + command + "\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
