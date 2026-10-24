using System.Globalization;

namespace PowerTools.Modules.DiskAnalyzer;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>Binary units (1 KB = 1024 B), one decimal above bytes, always '.' as separator.</summary>
    public static string Format(long bytes)
    {
        var value = Math.Abs((double)bytes);
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var number = value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
        return (bytes < 0 ? "-" : "") + number + " " + Units[unit];
    }

    /// <summary>Like <see cref="Format"/> but prefixes growth with '+', for showing size changes.</summary>
    public static string FormatSigned(long bytes) => bytes > 0 ? "+" + Format(bytes) : Format(bytes);
}
