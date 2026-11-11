using System;
using System.Collections.Generic;
using WinTab.Core.Audio;
using WinTab.Core.Cleaner;
using WinTab.Core.Monitor;

namespace WinTab.App;

/// <summary>
/// Made-up data used only by the --demo switch, so the screenshots in the
/// README never show the real apps, files or folders on the machine they were
/// taken on.
/// </summary>
internal static class Demo
{
    public static bool Enabled { get; set; }

    public static IReadOnlyList<AppAudioSession> Sessions { get; } = new[]
    {
        new AppAudioSession(1001, "Music player", 0.62f, true, false),
        new AppAudioSession(1002, "Web browser", 0.80f, true, false),
        new AppAudioSession(1003, "Video call", 0.45f, false, false),
        new AppAudioSession(1004, "Game", 0.30f, false, true),
    };

    public static SystemSnapshot Snapshot { get; } = new(
        CpuFraction: 0.18, MemoryUsedBytes: 9_400_000_000, MemoryTotalBytes: 16_000_000_000,
        SystemDriveUsedFraction: 0.64, SystemDriveFreeBytes: 180_000_000_000, SystemDriveName: "C:",
        DiskActivityFraction: 0.04, NetDownBytesPerSecond: 1_250_000, NetUpBytesPerSecond: 85_000);

    public static IReadOnlyList<CleanCategory> Categories()
    {
        return new List<CleanCategory>
        {
            new() { Id = "user-temp", Title = "Temporary files", Description = "Files programs left in your temp folder, older than a day.", Size = 1_340_000_000, FileCount = 5120 },
            new() { Id = "windows-temp", Title = "Windows temporary files", Description = "The system temp folder. Files that need administrator rights are skipped.", Size = 220_000_000, FileCount = 310 },
            new() { Id = "browser-caches", Title = "Browser caches", Description = "Cached web pages and images. Not history, cookies or passwords. Close the browser first.", Size = 2_050_000_000, FileCount = 18400 },
            new() { Id = "crash-dumps", Title = "Crash dumps and error reports", Description = "Memory dumps and Windows Error Reporting files.", Size = 480_000_000, FileCount = 12 },
            new() { Id = "thumbnails", Title = "Thumbnail cache", Description = "Explorer's picture previews. They are rebuilt when needed.", Size = 96_000_000, FileCount = 6 },
            new() { Id = "recycle-bin", Title = "Recycle Bin", Description = "Everything in the Recycle Bin. This cannot be undone.", Size = 730_000_000, FileCount = 41, Selected = false, IsRecycleBin = true },
        };
    }

    public static IReadOnlyList<InstalledApp> Apps { get; } = new[]
    {
        new InstalledApp { Name = "Sample Photo Editor", Publisher = "Example Software", Version = "5.2.1", SizeBytes = 1_200_000_000, UninstallCommand = "x" },
        new InstalledApp { Name = "Sample Video Player", Publisher = "Example Media", Version = "3.0.24", SizeBytes = 190_000_000, UninstallCommand = "x" },
        new InstalledApp { Name = "Sample Code Editor", Publisher = "Example Tools", Version = "1.95.0", SizeBytes = 420_000_000, UninstallCommand = "x" },
        new InstalledApp { Name = "Sample Chat Client", Publisher = "Example Chat Inc.", Version = "2.1.8", SizeBytes = 310_000_000, UninstallCommand = "x" },
        new InstalledApp { Name = "Sample Game Launcher", Publisher = "Example Games", Version = "11.4", SizeBytes = 2_800_000_000, UninstallCommand = "x" },
    };

    public static IReadOnlyList<LargeFile> LargeFiles { get; } = new[]
    {
        new LargeFile { Path = @"C:\Users\you\Videos\holiday-recording.mp4", Size = 4_800_000_000 },
        new LargeFile { Path = @"C:\Users\you\Downloads\operating-system-image.iso", Size = 3_900_000_000 },
        new LargeFile { Path = @"C:\Users\you\Documents\project-backup.zip", Size = 1_700_000_000 },
        new LargeFile { Path = @"C:\Users\you\Downloads\installer-large.exe", Size = 980_000_000 },
        new LargeFile { Path = @"C:\Users\you\Pictures\raw-photos-2023.zip", Size = 640_000_000 },
    };
}
