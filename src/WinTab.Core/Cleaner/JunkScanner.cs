using System.Runtime.InteropServices;

namespace WinTab.Core.Cleaner;

/// <summary>One group of junk the cleaner can remove. Sizes are measured, never guessed.</summary>
public sealed class CleanCategory
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>Folders whose contents belong to this category.</summary>
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();

    /// <summary>Only files older than this are touched (a freshly written temp file may still be in use).</summary>
    public TimeSpan MinimumAge { get; init; } = TimeSpan.Zero;

    /// <summary>True for the Recycle Bin, which is emptied through the shell instead of file by file.</summary>
    public bool IsRecycleBin { get; init; }

    public long Size { get; set; }
    public int FileCount { get; set; }
    public bool Selected { get; set; } = true;
}

public sealed record CleanResult(long FreedBytes, int RemovedFiles, int FailedFiles);

/// <summary>
/// Finds and removes safe-to-delete junk: temporary files, browser caches, crash
/// dumps and error reports, thumbnail caches and the Recycle Bin. It never
/// touches documents, browser history, passwords or cookies.
/// </summary>
public static class JunkScanner
{
    public static IReadOnlyList<CleanCategory> BuildCategories(
        string? localAppData = null, string? tempPath = null, string? windowsDir = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        tempPath ??= Path.GetTempPath();
        windowsDir ??= Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var browserRoots = new List<string>();
        AddProfileCaches(browserRoots, Path.Combine(localAppData, "Google", "Chrome", "User Data"));
        AddProfileCaches(browserRoots, Path.Combine(localAppData, "Microsoft", "Edge", "User Data"));
        AddProfileCaches(browserRoots, Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"));
        string firefox = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox))
        {
            foreach (var profile in SafeDirs(firefox)) browserRoots.Add(Path.Combine(profile, "cache2"));
        }

        return new List<CleanCategory>
        {
            new()
            {
                Id = "user-temp", Title = "Temporary files",
                Description = "Files programs left in your temp folder, older than a day.",
                Roots = new[] { tempPath }, MinimumAge = TimeSpan.FromDays(1),
            },
            new()
            {
                Id = "windows-temp", Title = "Windows temporary files",
                Description = "The system temp folder. Files that need administrator rights are skipped.",
                Roots = new[] { Path.Combine(windowsDir, "Temp") }, MinimumAge = TimeSpan.FromDays(1),
            },
            new()
            {
                Id = "browser-caches", Title = "Browser caches",
                Description = "Cached web pages and images of Chrome, Edge, Brave and Firefox. Not history, cookies or passwords. Close the browser first.",
                Roots = browserRoots,
            },
            new()
            {
                Id = "crash-dumps", Title = "Crash dumps and error reports",
                Description = "Memory dumps and Windows Error Reporting files.",
                Roots = new[]
                {
                    Path.Combine(localAppData, "CrashDumps"),
                    Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportQueue"),
                },
            },
            new()
            {
                Id = "thumbnails", Title = "Thumbnail cache",
                Description = "Explorer's picture previews. They are rebuilt when needed.",
                Roots = new[] { Path.Combine(localAppData, "Microsoft", "Windows", "Explorer") },
            },
            new()
            {
                Id = "recycle-bin", Title = "Recycle Bin",
                Description = "Everything in the Recycle Bin. This cannot be undone.",
                IsRecycleBin = true, Selected = false,
            },
        };
    }

    private static void AddProfileCaches(List<string> roots, string userData)
    {
        if (!Directory.Exists(userData)) return;
        foreach (var profile in SafeDirs(userData))
        {
            string name = Path.GetFileName(profile);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            roots.Add(Path.Combine(profile, "Cache"));
            roots.Add(Path.Combine(profile, "Code Cache"));
            roots.Add(Path.Combine(profile, "GPUCache"));
        }
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Measures every category. Safe to call repeatedly; nothing is changed.</summary>
    public static void Measure(IEnumerable<CleanCategory> categories, CancellationToken cancel = default)
    {
        foreach (var category in categories)
        {
            cancel.ThrowIfCancellationRequested();
            category.Size = 0;
            category.FileCount = 0;
            if (category.IsRecycleBin)
            {
                category.Size = RecycleBin.Size(out int items);
                category.FileCount = items;
                continue;
            }
            var cutoff = DateTime.UtcNow - category.MinimumAge;
            foreach (var root in category.Roots)
            {
                foreach (var file in EnumerateFiles(root))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc > cutoff) continue;
                        category.Size += info.Length;
                        category.FileCount++;
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    /// <summary>Deletes the chosen categories. Files in use or without permission are skipped and counted.</summary>
    public static CleanResult Clean(IEnumerable<CleanCategory> categories, CancellationToken cancel = default)
    {
        long freed = 0;
        int removed = 0, failed = 0;
        foreach (var category in categories.Where(c => c.Selected))
        {
            cancel.ThrowIfCancellationRequested();
            if (category.IsRecycleBin)
            {
                long before = RecycleBin.Size(out int items);
                if (RecycleBin.Empty())
                {
                    freed += before;
                    removed += items;
                }
                else
                {
                    failed++;
                }
                continue;
            }

            var cutoff = DateTime.UtcNow - category.MinimumAge;
            foreach (var root in category.Roots)
            {
                foreach (var file in EnumerateFiles(root))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc > cutoff) continue;
                        long length = info.Length;
                        if (info.IsReadOnly) info.IsReadOnly = false;
                        info.Delete();
                        freed += length;
                        removed++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
                RemoveEmptyDirectories(root);
            }
        }
        return new CleanResult(freed, removed, failed);
    }

    /// <summary>Every file below <paramref name="root"/>; links are not followed and unreadable folders are skipped.</summary>
    public static IEnumerable<string> EnumerateFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.GetFiles(current);
                dirs = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }
            foreach (var file in files) yield return file;
            foreach (var dir in dirs)
            {
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch
                {
                    continue;
                }
                pending.Push(dir);
            }
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}

internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ShQueryRbInfo
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    private const uint NoConfirmation = 0x1;
    private const uint NoProgressUi = 0x2;
    private const uint NoSound = 0x4;

    public static long Size(out int items)
    {
        var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        items = 0;
        try
        {
            if (SHQueryRecycleBin(null, ref info) != 0) return 0;
            items = (int)Math.Min(info.i64NumItems, int.MaxValue);
            return info.i64Size;
        }
        catch
        {
            return 0;
        }
    }

    public static bool Empty()
    {
        try
        {
            // S_OK, or "already empty".
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
            return hr == 0 || hr == unchecked((int)0x8000FFFF) || hr == 1;
        }
        catch
        {
            return false;
        }
    }
}
