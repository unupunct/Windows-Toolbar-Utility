using System.Diagnostics;
using System.Text.Json;

namespace WinTab.Core.SendTo;

/// <summary>A place files can go: a folder (including a drive or network share) or an app that opens them.</summary>
public sealed class SendToDestination
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsApp { get; set; }
}

public sealed record SendResult(int Sent, IReadOnlyList<string> Failures);

/// <summary>
/// "Send to": a folder destination receives a copy of the files, an app
/// destination opens them. The list is the person's own and lives in a small
/// JSON file in their profile.
/// </summary>
public sealed class SendToService
{
    private readonly string _file;
    private readonly List<SendToDestination> _destinations = new();

    public SendToService(string? dataFolder = null)
    {
        dataFolder ??= System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnupunctWinTab");
        Directory.CreateDirectory(dataFolder);
        _file = System.IO.Path.Combine(dataFolder, "sendto.json");
        Load();
    }

    public IReadOnlyList<SendToDestination> Destinations => _destinations;

    private void Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var saved = JsonSerializer.Deserialize<List<SendToDestination>>(File.ReadAllText(_file));
                if (saved != null)
                {
                    _destinations.AddRange(saved);
                    return;
                }
            }
        }
        catch
        {
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _destinations.Add(new SendToDestination { Name = "Desktop", Path = System.IO.Path.Combine(profile, "Desktop") });
        _destinations.Add(new SendToDestination { Name = "Downloads", Path = System.IO.Path.Combine(profile, "Downloads") });
        Save();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_file, JsonSerializer.Serialize(_destinations, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    public bool Add(string name, string path, bool isApp)
    {
        if (_destinations.Any(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase))) return false;
        _destinations.Add(new SendToDestination { Name = name, Path = path, IsApp = isApp });
        Save();
        return true;
    }

    public void Remove(SendToDestination destination)
    {
        _destinations.Remove(destination);
        Save();
    }

    public SendResult Send(IEnumerable<string> files, SendToDestination destination)
    {
        var list = files.ToList();
        var failures = new List<string>();
        if (destination.IsApp)
        {
            if (!File.Exists(destination.Path))
                return new SendResult(0, new[] { $"{destination.Name} is not installed at {destination.Path}." });
            try
            {
                var start = new ProcessStartInfo(destination.Path) { UseShellExecute = false };
                foreach (var file in list) start.ArgumentList.Add(file);
                Process.Start(start);
                return new SendResult(list.Count, failures);
            }
            catch (Exception ex)
            {
                return new SendResult(0, new[] { $"Could not open the files with {destination.Name}: {ex.Message}" });
            }
        }

        if (!Directory.Exists(destination.Path))
            return new SendResult(0, new[] { $"{destination.Name} is not available. If it is a network location, connect it first." });

        int sent = 0;
        foreach (var file in list)
        {
            try
            {
                if (Directory.Exists(file))
                {
                    CopyDirectory(file, UniquePath(destination.Path, System.IO.Path.GetFileName(file)));
                }
                else
                {
                    File.Copy(file, UniquePath(destination.Path, System.IO.Path.GetFileName(file)));
                }
                sent++;
            }
            catch (Exception ex)
            {
                failures.Add($"{System.IO.Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return new SendResult(sent, failures);
    }

    /// <summary>A path inside <paramref name="folder"/> that does not exist yet ("name 2.ext", "name 3.ext", ...).</summary>
    public static string UniquePath(string folder, string fileName)
    {
        string candidate = System.IO.Path.Combine(folder, fileName);
        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string extension = System.IO.Path.GetExtension(fileName);
        int counter = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = System.IO.Path.Combine(folder, $"{baseName} {counter}{extension}");
            counter++;
        }
        return candidate;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, System.IO.Path.Combine(target, System.IO.Path.GetFileName(dir)));
    }
}
