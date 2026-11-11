using WinTab.Core;
using WinTab.Core.Cleaner;
using WinTab.Core.SendTo;
using Xunit;

namespace WinTab.Tests;

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wintab-test-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Write(string relative, int bytes, DateTime? lastWrite = null)
    {
        string full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
        if (lastWrite != null) File.SetLastWriteTimeUtc(full, lastWrite.Value);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    public void BytesAreReadable(long bytes, string expected)
    {
        Assert.Equal(expected.Replace('.', System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]),
            Fmt.Bytes(bytes));
    }

    [Fact]
    public void CompactRateUsesShortUnits()
    {
        Assert.EndsWith("B", Fmt.RateCompact(10));
        Assert.EndsWith("K", Fmt.RateCompact(5 * 1024));
        Assert.EndsWith("M", Fmt.RateCompact(3 * 1024 * 1024));
    }

    [Fact]
    public void NegativeValuesAreClamped()
    {
        Assert.Equal("0 B", Fmt.Bytes(-5));
        Assert.Equal("0%", Fmt.Percent(-1));
        Assert.Equal("100%", Fmt.Percent(5));
    }
}

public class SendToTests
{
    [Fact]
    public void UniquePathNumbersExistingNames()
    {
        using var folder = new TempFolder();
        folder.Write("report.txt", 1);
        folder.Write("report 2.txt", 1);
        Assert.Equal(System.IO.Path.Combine(folder.Path, "report 3.txt"),
            SendToService.UniquePath(folder.Path, "report.txt"));
    }

    [Fact]
    public void FilesAreCopiedAndTheSourceStays()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        using var data = new TempFolder();
        string file = source.Write("a.bin", 10);
        var service = new SendToService(data.Path);
        var result = service.Send(new[] { file }, new SendToDestination { Name = "T", Path = target.Path });
        Assert.Equal(1, result.Sent);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(System.IO.Path.Combine(target.Path, "a.bin")));
    }

    [Fact]
    public void SendingTwiceKeepsBothCopies()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        using var data = new TempFolder();
        string file = source.Write("a.bin", 10);
        var service = new SendToService(data.Path);
        var destination = new SendToDestination { Name = "T", Path = target.Path };
        service.Send(new[] { file }, destination);
        service.Send(new[] { file }, destination);
        Assert.True(File.Exists(System.IO.Path.Combine(target.Path, "a 2.bin")));
    }

    [Fact]
    public void MissingFolderIsReportedNotThrown()
    {
        using var source = new TempFolder();
        using var data = new TempFolder();
        string file = source.Write("a.bin", 10);
        var service = new SendToService(data.Path);
        var result = service.Send(new[] { file }, new SendToDestination { Name = "Gone", Path = @"Z:\definitely\missing" });
        Assert.Equal(0, result.Sent);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void DestinationsPersistAndRemove()
    {
        using var data = new TempFolder();
        var first = new SendToService(data.Path);
        Assert.True(first.Add("Share", @"\\server\share", isApp: false));
        Assert.False(first.Add("Share again", @"\\server\share", isApp: false));
        var second = new SendToService(data.Path);
        var share = second.Destinations.Single(d => d.Name == "Share");
        second.Remove(share);
        Assert.DoesNotContain(new SendToService(data.Path).Destinations, d => d.Name == "Share");
    }
}

public class CleanerTests
{
    private static CleanCategory Category(string root, TimeSpan age) => new()
    {
        Id = "t", Title = "t", Description = "t", Roots = new[] { root }, MinimumAge = age,
    };

    [Fact]
    public void MeasureCountsOnlyOldFiles()
    {
        using var folder = new TempFolder();
        folder.Write("old.tmp", 100, DateTime.UtcNow.AddDays(-3));
        folder.Write("sub\\older.tmp", 50, DateTime.UtcNow.AddDays(-5));
        folder.Write("fresh.tmp", 1000);
        var category = Category(folder.Path, TimeSpan.FromDays(1));
        JunkScanner.Measure(new[] { category });
        Assert.Equal(150, category.Size);
        Assert.Equal(2, category.FileCount);
    }

    [Fact]
    public void CleanRemovesOldFilesAndKeepsFreshOnes()
    {
        using var folder = new TempFolder();
        string old = folder.Write("old.tmp", 100, DateTime.UtcNow.AddDays(-3));
        string fresh = folder.Write("fresh.tmp", 100);
        var result = JunkScanner.Clean(new[] { Category(folder.Path, TimeSpan.FromDays(1)) });
        Assert.Equal(100, result.FreedBytes);
        Assert.Equal(1, result.RemovedFiles);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void UnselectedCategoriesAreNotTouched()
    {
        using var folder = new TempFolder();
        string old = folder.Write("old.tmp", 100, DateTime.UtcNow.AddDays(-3));
        var category = Category(folder.Path, TimeSpan.FromDays(1));
        category.Selected = false;
        var result = JunkScanner.Clean(new[] { category });
        Assert.Equal(0, result.RemovedFiles);
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void EmptyFoldersLeftBehindAreRemoved()
    {
        using var folder = new TempFolder();
        folder.Write("a\\b\\old.tmp", 10, DateTime.UtcNow.AddDays(-3));
        JunkScanner.Clean(new[] { Category(folder.Path, TimeSpan.FromDays(1)) });
        Assert.False(Directory.Exists(System.IO.Path.Combine(folder.Path, "a")));
    }

    [Fact]
    public void MissingRootIsNotAnError()
    {
        var category = Category(@"Z:\nothing\here", TimeSpan.Zero);
        JunkScanner.Measure(new[] { category });
        Assert.Equal(0, category.Size);
        Assert.Equal(0, JunkScanner.Clean(new[] { category }).RemovedFiles);
    }

    [Fact]
    public void BrowserCachesNeverIncludeHistoryOrCookies()
    {
        using var local = new TempFolder();
        local.Write("Google\\Chrome\\User Data\\Default\\Cache\\Cache_Data\\f_1", 5);
        local.Write("Google\\Chrome\\User Data\\Default\\Cookies", 5);
        local.Write("Google\\Chrome\\User Data\\Default\\History", 5);
        var categories = JunkScanner.BuildCategories(local.Path, local.Path, local.Path);
        var browser = categories.Single(c => c.Id == "browser-caches");
        JunkScanner.Clean(new[] { browser });
        Assert.False(File.Exists(System.IO.Path.Combine(local.Path, "Google\\Chrome\\User Data\\Default\\Cache\\Cache_Data\\f_1")));
        Assert.True(File.Exists(System.IO.Path.Combine(local.Path, "Google\\Chrome\\User Data\\Default\\Cookies")));
        Assert.True(File.Exists(System.IO.Path.Combine(local.Path, "Google\\Chrome\\User Data\\Default\\History")));
    }

    [Fact]
    public void RecycleBinIsNotSelectedByDefault()
    {
        var bin = JunkScanner.BuildCategories().Single(c => c.IsRecycleBin);
        Assert.False(bin.Selected);
    }
}

public class LargeFilesTests
{
    [Fact]
    public void OnlyFilesAboveTheLimitAreListedBiggestFirst()
    {
        using var folder = new TempFolder();
        folder.Write("small.bin", 100);
        folder.Write("big.bin", 5000);
        folder.Write("sub\\bigger.bin", 9000);
        var found = LargeFilesScanner.Scan(new[] { folder.Path }, 1000);
        Assert.Equal(new[] { "bigger.bin", "big.bin" }, found.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void ResultsAreCapped()
    {
        using var folder = new TempFolder();
        for (int i = 0; i < 10; i++) folder.Write($"f{i}.bin", 2000 + i);
        Assert.Equal(3, LargeFilesScanner.Scan(new[] { folder.Path }, 1000, maxResults: 3).Count);
    }
}
