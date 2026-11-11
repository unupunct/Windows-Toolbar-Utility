using Microsoft.VisualBasic.FileIO;

namespace WinTab.Core.Cleaner;

public sealed class LargeFile
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public bool Selected { get; set; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>
/// Finds the biggest files in the folders people keep things in, so they can
/// decide what to remove. It only reads sizes; removal is a separate, explicit
/// step and moves files to the Recycle Bin.
/// </summary>
public static class LargeFilesScanner
{
    public static IReadOnlyList<string> DefaultRoots()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { "Downloads", "Desktop", "Documents", "Videos", "Music", "Pictures" }
            .Select(name => System.IO.Path.Combine(profile, name))
            .ToList();
    }

    public static IReadOnlyList<LargeFile> Scan(IEnumerable<string> roots, long minimumBytes,
        int maxResults = 300, CancellationToken cancel = default)
    {
        var found = new List<LargeFile>();
        foreach (var root in roots)
        {
            foreach (var file in JunkScanner.EnumerateFiles(root))
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    long length = new FileInfo(file).Length;
                    if (length >= minimumBytes) found.Add(new LargeFile { Path = file, Size = length });
                }
                catch
                {
                }
            }
        }
        return found.OrderByDescending(f => f.Size).Take(maxResults).ToList();
    }

    /// <summary>Moves one file to the Recycle Bin. Returns false when it could not be moved.</summary>
    public static bool MoveToRecycleBin(string path)
    {
        try
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
