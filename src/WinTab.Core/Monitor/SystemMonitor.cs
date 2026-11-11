using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WinTab.Core.Monitor;

public sealed record SystemSnapshot(
    double CpuFraction,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    double SystemDriveUsedFraction,
    long SystemDriveFreeBytes,
    string SystemDriveName,
    double DiskActivityFraction,
    double NetDownBytesPerSecond,
    double NetUpBytesPerSecond)
{
    public double MemoryFraction => MemoryTotalBytes == 0 ? 0 : (double)MemoryUsedBytes / MemoryTotalBytes;
}

/// <summary>CPU, memory, system drive and network readings. Call <see cref="Sample"/> about once a second.</summary>
public sealed class SystemMonitor : IDisposable
{
    private PerformanceCounter? _cpu;
    private PerformanceCounter? _disk;
    private long _lastRx;
    private long _lastTx;
    private DateTime _lastNetSample = DateTime.MinValue;

    public SystemMonitor()
    {
        try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpu.NextValue(); }
        catch { _cpu = null; }
        try { _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total"); _disk.NextValue(); }
        catch { _disk = null; }
    }

    public SystemSnapshot Sample()
    {
        double cpu = Read(_cpu) / 100.0;
        double disk = Read(_disk) / 100.0;
        var (used, total) = ReadMemory();
        var (driveName, driveUsed, driveFree) = ReadSystemDrive();
        var (down, up) = ReadNetwork();
        return new SystemSnapshot(Math.Clamp(cpu, 0, 1), used, total, driveUsed, driveFree, driveName,
            Math.Clamp(disk, 0, 1), down, up);
    }

    private static double Read(PerformanceCounter? counter)
    {
        try { return counter?.NextValue() ?? 0; }
        catch { return 0; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static (long used, long total) ReadMemory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
        return ((long)(status.ullTotalPhys - status.ullAvailPhys), (long)status.ullTotalPhys);
    }

    private static (string name, double usedFraction, long free) ReadSystemDrive()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize == 0) return (root, 0, 0);
            double used = 1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize;
            return (root.TrimEnd('\\'), used, drive.AvailableFreeSpace);
        }
        catch
        {
            return ("C:", 0, 0);
        }
    }

    private (double down, double up) ReadNetwork()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch
        {
            return (0, 0);
        }

        var now = DateTime.UtcNow;
        double down = 0, up = 0;
        if (_lastNetSample != DateTime.MinValue)
        {
            double seconds = (now - _lastNetSample).TotalSeconds;
            if (seconds > 0.05)
            {
                down = Math.Max(0, (rx - _lastRx) / seconds);
                up = Math.Max(0, (tx - _lastTx) / seconds);
            }
        }
        _lastRx = rx;
        _lastTx = tx;
        _lastNetSample = now;
        return (down, up);
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _disk?.Dispose();
    }
}
