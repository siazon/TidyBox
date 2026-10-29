namespace TidyBox.App;

/// <summary>Real total capacity of the machine's fixed drives, for showing proportions (e.g. Recycle Bin usage as a share of total disk space).</summary>
internal static class DiskCapacityHelper
{
    public static long TotalFixedDriveBytes()
        => DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Sum(d => d.TotalSize);
}
