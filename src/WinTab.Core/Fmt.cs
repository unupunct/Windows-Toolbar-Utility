using System.Globalization;

namespace WinTab.Core;

/// <summary>Small formatting helpers shared by the app and the tests.</summary>
public static class Fmt
{
    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? string.Format(CultureInfo.CurrentCulture, "{0:0} {1}", value, units[unit])
            : string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", value, units[unit]);
    }

    /// <summary>A short rate such as "1.2M" or "340K", for the tray strip.</summary>
    public static string RateCompact(double bytesPerSecond)
    {
        if (bytesPerSecond < 0) bytesPerSecond = 0;
        if (bytesPerSecond < 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0}B", bytesPerSecond);
        if (bytesPerSecond < 1024 * 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0}K", bytesPerSecond / 1024);
        if (bytesPerSecond < 1024L * 1024 * 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0.0}M", bytesPerSecond / (1024 * 1024));
        return string.Format(CultureInfo.CurrentCulture, "{0:0.0}G", bytesPerSecond / (1024.0 * 1024 * 1024));
    }

    public static string Rate(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

    public static string Percent(double fraction)
        => string.Format(CultureInfo.CurrentCulture, "{0:0}%", Math.Clamp(fraction, 0, 1) * 100);
}
