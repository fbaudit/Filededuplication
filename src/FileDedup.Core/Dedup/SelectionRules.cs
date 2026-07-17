using FileDedup.Core.Scanning;

namespace FileDedup.Core.Dedup;

/// <summary>그룹에서 어떤 파일을 보존할지 결정하는 정책 (DoubleKiller selection wizard 벤치마킹).</summary>
public enum KeepPolicy
{
    /// <summary>수정일이 가장 최신인 파일 보존.</summary>
    Newest,
    /// <summary>수정일이 가장 오래된 파일 보존.</summary>
    Oldest,
    /// <summary>전체 경로가 가장 짧은 파일 보존.</summary>
    ShortestPath,
    /// <summary>지정 폴더 아래의 파일 우선 보존 (없으면 최신 보존).</summary>
    PreferFolder,
}

/// <summary>
/// 자동 선택 규칙. 반환값은 "삭제 대상으로 표시할" 파일 집합이며,
/// 어떤 규칙을 쓰더라도 그룹당 최소 1개는 반드시 보존된다.
/// </summary>
public static class SelectionRules
{
    public static HashSet<FileRecord> Apply(
        IEnumerable<DuplicateGroup> groups, KeepPolicy policy, string? preferredFolder = null)
    {
        var toRemove = new HashSet<FileRecord>();
        foreach (var group in groups)
        {
            var keeper = PickKeeper(group.Files, policy, preferredFolder);
            foreach (var file in group.Files)
                if (!ReferenceEquals(file, keeper))
                    toRemove.Add(file);
        }
        return toRemove;
    }

    /// <summary>글롭 패턴에 맞는 파일을 삭제 대상으로 표시하되, 그룹 전체가 지워지지 않게 보호.</summary>
    public static HashSet<FileRecord> SelectByPattern(
        IEnumerable<DuplicateGroup> groups, string pattern)
    {
        var matcher = new GlobMatcher([pattern]);
        var toRemove = new HashSet<FileRecord>();
        foreach (var group in groups)
        {
            var matched = group.Files
                .Where(f => matcher.IsMatch(f.FullPath, f.Entry.Name))
                .ToList();
            // 그룹 전체가 매칭되면 1개(최신)는 보존
            if (matched.Count == group.Files.Count)
            {
                var keeper = matched.MaxBy(f => f.Entry.ModifiedUtc)!;
                matched.Remove(keeper);
            }
            foreach (var file in matched) toRemove.Add(file);
        }
        return toRemove;
    }

    private static FileRecord PickKeeper(
        IReadOnlyList<FileRecord> files, KeepPolicy policy, string? preferredFolder)
    {
        switch (policy)
        {
            case KeepPolicy.Newest:
                return files.MaxBy(f => f.Entry.ModifiedUtc)!;
            case KeepPolicy.Oldest:
                return files.MinBy(f => f.Entry.ModifiedUtc)!;
            case KeepPolicy.ShortestPath:
                return files.MinBy(f => f.FullPath.Length)!;
            case KeepPolicy.PreferFolder:
                if (!string.IsNullOrEmpty(preferredFolder))
                {
                    var prefix = Path.TrimEndingDirectorySeparator(preferredFolder)
                                 + Path.DirectorySeparatorChar;
                    var inFolder = files
                        .Where(f => f.FullPath.StartsWith(prefix, OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                        .ToList();
                    if (inFolder.Count > 0)
                        return inFolder.MaxBy(f => f.Entry.ModifiedUtc)!;
                }
                return files.MaxBy(f => f.Entry.ModifiedUtc)!;
            default:
                throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }
}
