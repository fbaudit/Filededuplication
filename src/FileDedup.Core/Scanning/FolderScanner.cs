namespace FileDedup.Core.Scanning;

public sealed record ScanProgress(int FilesFound, string CurrentPath);

/// <summary>지정 폴더(들)를 재귀 열거해 필터를 통과한 파일 목록을 만든다.</summary>
public static class FolderScanner
{
    public static Task<List<FileEntry>> ScanAsync(
        IEnumerable<string> roots,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() => Scan(roots, options, progress, ct), ct);

    public static List<FileEntry> Scan(
        IEnumerable<string> roots,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var exclude = new GlobMatcher(options.ExcludePatterns);
        var results = new List<FileEntry>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubdirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = options.SkipHiddenAndSystem
                ? FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
                : FileAttributes.ReparsePoint,
        };

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;

            foreach (var path in Directory.EnumerateFiles(root, "*", enumOptions))
            {
                ct.ThrowIfCancellationRequested();
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (!info.Exists) continue;
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                if (info.Length < options.MinFileSize || info.Length > options.MaxFileSize) continue;
                if (!exclude.IsEmpty && exclude.IsMatch(path, info.Name)) continue;
                if (!seen.Add(info.FullName)) continue;

                results.Add(new FileEntry(
                    info.FullName, info.Name, info.Length,
                    info.CreationTimeUtc, info.LastWriteTimeUtc));

                if (results.Count % 500 == 0)
                    progress?.Report(new ScanProgress(results.Count, path));
            }
        }

        progress?.Report(new ScanProgress(results.Count, string.Empty));
        return results;
    }
}
