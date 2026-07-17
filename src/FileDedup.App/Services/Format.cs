namespace FileDedup.App.Services;

public static class Format
{
    public static string Bytes(long bytes)
    {
        if (bytes < 0) return "";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    public static string LocalTime(DateTime utc)
        => utc == DateTime.MinValue ? "" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
