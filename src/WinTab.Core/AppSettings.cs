using System.Text.Json;
using Microsoft.Win32;

namespace WinTab.Core;

/// <summary>The few settings the app keeps, stored as JSON in the user's profile.</summary>
public sealed class AppSettings
{
    public bool ShowTaskbarStrip { get; set; } = true;
    public bool StartWithWindows { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnupunctWinTab", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "UnupunctWinTab";

    /// <summary>Adds or removes this app's start-at-sign-in entry (per user, no administrator rights).</summary>
    public static void ApplyStartWithWindows(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enabled) key.SetValue(RunValue, "\"" + exePath + "\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch
        {
        }
    }
}
