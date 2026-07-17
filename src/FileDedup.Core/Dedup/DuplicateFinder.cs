using System.Collections.Concurrent;
using FileDedup.Core.Hashing;
using FileDedup.Core.Scanning;

namespace FileDedup.Core.Dedup;

public sealed record ReadError(string FullPath, string Message);

public sealed class DedupResult
{
    public required List<DuplicateGroup> Groups { get; init; }
    public required List<ReadError> Errors { get; init; }
}

/// <summary>
/// 중복 판별 엔진. 선택된 기준을 모두 만족하는 파일들을 그룹으로 묶는다.
/// 해시 기준이 있으면 크기 그룹핑 → 선두 64KB 부분해시 → 전체 해시의
/// 3단계 파이프라인으로 전체 해시 계산 대상을 최소화한다.
/// </summary>
public static class DuplicateFinder
{
    public static async Task<DedupResult> FindAsync(
        IReadOnlyList<FileEntry> files,
        DedupOptions options,
        IProgress<DedupProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (options.Criteria == DuplicateCriteria.None)
            throw new ArgumentException("중복 판별 기준이 하나 이상 선택되어야 합니다.", nameof(options));

        var errors = new ConcurrentBag<ReadError>();
        progress?.Report(new DedupProgress(DedupStage.Grouping, 0, files.Count, 0));

        // 1단계: 메타데이터 기준(이름/크기/날짜)으로 1차 그룹핑.
        //        해시 기준이 있으면 내용 동일 ⟹ 크기 동일이므로 크기를 항상 키에 포함(순수 최적화).
        var needHash = options.Criteria.RequiresContentHash();
        var metaGroups = files
            .GroupBy(f => MetadataKey(f, options, includeSize: needHash))
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

        if (!needHash)
        {
            var plainGroups = metaGroups
                .Select(g => new DuplicateGroup
                {
                    Files = g.Select(f => new FileRecord { Entry = f }).ToList(),
                })
                .OrderByDescending(g => g.WastedBytes)
                .ToList();
            progress?.Report(new DedupProgress(DedupStage.Done, files.Count, files.Count, 0));
            return new DedupResult { Groups = plainGroups, Errors = [.. errors] };
        }

        var parallelism = options.DegreeOfParallelism > 0
            ? options.DegreeOfParallelism
            : Environment.ProcessorCount;

        // 2단계: 부분해시(선두 64KB)로 후보 축소. 64KB 이하 파일은 전체 해시로 직행.
        var partialCandidates = metaGroups
            .Where(g => g[0].Length > FileHasher.PartialHashLength)
            .SelectMany(g => g)
            .ToList();
        var partialHashes = new ConcurrentDictionary<string, ulong>();
        var processed = 0;
        long processedBytes = 0;

        await Parallel.ForEachAsync(partialCandidates,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (entry, token) =>
            {
                try
                {
                    partialHashes[entry.FullPath] = await FileHasher.ComputePartialAsync(entry.FullPath, token);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new ReadError(entry.FullPath, ex.Message));
                }
                var done = Interlocked.Increment(ref processed);
                Interlocked.Add(ref processedBytes, Math.Min(entry.Length, FileHasher.PartialHashLength));
                if (done % 50 == 0)
                    progress?.Report(new DedupProgress(
                        DedupStage.PartialHashing, done, partialCandidates.Count, processedBytes));
            });

        var fullCandidateGroups = new List<List<FileEntry>>();
        foreach (var group in metaGroups)
        {
            if (group[0].Length <= FileHasher.PartialHashLength)
            {
                fullCandidateGroups.Add(group);
                continue;
            }
            fullCandidateGroups.AddRange(group
                .Where(f => partialHashes.ContainsKey(f.FullPath))
                .GroupBy(f => partialHashes[f.FullPath])
                .Where(g => g.Count() > 1)
                .Select(g => g.ToList()));
        }

        // 3단계: 남은 후보만 선택된 알고리즘으로 전체 해시 계산.
        var algorithms = options.Criteria.ToHashAlgorithms();
        var fullCandidates = fullCandidateGroups.SelectMany(g => g).ToList();
        var records = new ConcurrentDictionary<string, FileRecord>();
        processed = 0;
        processedBytes = 0;

        await Parallel.ForEachAsync(fullCandidates,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (entry, token) =>
            {
                try
                {
                    var hashes = await FileHasher.ComputeAsync(entry.FullPath, algorithms, token);
                    records[entry.FullPath] = new FileRecord { Entry = entry, Hashes = hashes };
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new ReadError(entry.FullPath, ex.Message));
                }
                var done = Interlocked.Increment(ref processed);
                Interlocked.Add(ref processedBytes, entry.Length);
                if (done % 20 == 0)
                    progress?.Report(new DedupProgress(
                        DedupStage.FullHashing, done, fullCandidates.Count, processedBytes));
            });

        var groups = fullCandidateGroups
            .SelectMany(g => g
                .Where(f => records.ContainsKey(f.FullPath))
                .Select(f => records[f.FullPath])
                .GroupBy(r => HashKey(r.Hashes, options.Criteria))
                .Where(sub => sub.Count() > 1))
            .Select(sub => new DuplicateGroup { Files = sub.ToList() })
            .OrderByDescending(g => g.WastedBytes)
            .ToList();

        progress?.Report(new DedupProgress(DedupStage.Done, files.Count, files.Count, processedBytes));
        return new DedupResult { Groups = groups, Errors = [.. errors] };
    }

    private static string MetadataKey(FileEntry f, DedupOptions options, bool includeSize)
    {
        var c = options.Criteria;
        var parts = new List<string>(4);
        if (c.HasFlag(DuplicateCriteria.FileName))
            parts.Add(options.IgnoreCase ? f.Name.ToUpperInvariant() : f.Name);
        if (includeSize || c.HasFlag(DuplicateCriteria.Size))
            parts.Add(f.Length.ToString());
        if (c.HasFlag(DuplicateCriteria.CreatedDate))
            parts.Add(TimeBucket(f.CreatedUtc, options.TimestampToleranceSeconds));
        if (c.HasFlag(DuplicateCriteria.ModifiedDate))
            parts.Add(TimeBucket(f.ModifiedUtc, options.TimestampToleranceSeconds));
        return string.Join("", parts);
    }

    // 허용 오차 크기의 버킷으로 절사해 비교 (FAT 2초 정밀도 등 흡수).
    private static string TimeBucket(DateTime utc, int toleranceSeconds)
    {
        if (toleranceSeconds <= 0) return utc.Ticks.ToString();
        var bucketTicks = TimeSpan.FromSeconds(toleranceSeconds).Ticks;
        return (utc.Ticks / bucketTicks).ToString();
    }

    private static string HashKey(FileHashes h, DuplicateCriteria c)
    {
        var parts = new List<string>(3);
        if (c.HasFlag(DuplicateCriteria.Crc32)) parts.Add(h.Crc32 ?? string.Empty);
        if (c.HasFlag(DuplicateCriteria.Md5)) parts.Add(h.Md5 ?? string.Empty);
        if (c.HasFlag(DuplicateCriteria.Sha256)) parts.Add(h.Sha256 ?? string.Empty);
        return string.Join("", parts);
    }
}
