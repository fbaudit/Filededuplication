using System.Collections.Concurrent;
using FileDedup.Core.Scanning;

namespace FileDedup.Core.Search;

public sealed record SearchHit(string FullPath, string Name, long Size, DateTime ModifiedUtc);

/// <summary>
/// 메모리 상주 파일명 인덱스 (Everything 벤치마킹).
/// 파일명 배열 + 디렉터리 참조만 메모리에 유지하고, 타이핑 즉시 부분 문자열 검색을 수행한다.
/// 볼륨 MFT 열거 결과 또는 폴더 스캔 결과로 구축한다.
/// </summary>
public sealed class FileNameIndex
{
    private readonly record struct Item(string Name, int DirIndex, long Size, DateTime ModifiedUtc);

    private readonly Item[] _items;
    private readonly string[] _dirPaths;

    public int Count => _items.Length;

    private FileNameIndex(Item[] items, string[] dirPaths)
    {
        _items = items;
        _dirPaths = dirPaths;
    }

    /// <summary>폴더 스캔 결과로 인덱스 구축 (비관리자 폴백 모드).</summary>
    public static FileNameIndex FromEntries(IReadOnlyList<FileEntry> entries)
    {
        var dirIndexMap = new Dictionary<string, int>();
        var dirs = new List<string>();
        var items = new Item[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var dir = Path.GetDirectoryName(entry.FullPath) ?? string.Empty;
            if (!dirIndexMap.TryGetValue(dir, out var dirIndex))
            {
                dirIndex = dirs.Count;
                dirs.Add(dir);
                dirIndexMap[dir] = dirIndex;
            }
            items[i] = new Item(entry.Name, dirIndex, entry.Length, entry.ModifiedUtc);
        }
        return new FileNameIndex(items, [.. dirs]);
    }

    /// <summary>
    /// MFT 열거 결과(FRN 부모 체인)로 인덱스 구축.
    /// 디렉터리 전체 경로는 부모 체인을 따라 1회 계산해 캐시한다.
    /// </summary>
    public static FileNameIndex FromVolumeItems(string driveRoot, IReadOnlyList<VolumeItem> volumeItems)
    {
        var dirById = new Dictionary<ulong, VolumeItem>();
        foreach (var item in volumeItems)
            if (item.IsDirectory)
                dirById[item.Id] = item;

        var root = Path.TrimEndingDirectorySeparator(driveRoot);
        var pathCache = new Dictionary<ulong, string>();

        string ResolveDirPath(ulong id)
        {
            if (pathCache.TryGetValue(id, out var cached)) return cached;
            // 재귀 대신 반복: 부모 체인을 캐시에 닿을 때까지 수집
            var chain = new Stack<VolumeItem>();
            var current = id;
            string basePath = root;
            while (dirById.TryGetValue(current, out var dir))
            {
                if (pathCache.TryGetValue(current, out var known)) { basePath = known; break; }
                chain.Push(dir);
                if (dir.ParentId == current) break; // 루트 자기참조 방어
                current = dir.ParentId;
            }
            foreach (var dir in chain)
            {
                basePath = pathCache.TryGetValue(dir.Id, out var known)
                    ? known
                    : pathCache[dir.Id] = Path.Combine(
                        pathCache.TryGetValue(dir.ParentId, out var parent) ? parent : root,
                        dir.Name);
            }
            return pathCache.TryGetValue(id, out var result) ? result : basePath;
        }

        var dirIndexMap = new Dictionary<string, int>();
        var dirs = new List<string>();
        var items = new List<Item>(volumeItems.Count);
        foreach (var item in volumeItems)
        {
            if (item.IsDirectory) continue;
            var dirPath = dirById.ContainsKey(item.ParentId) ? ResolveDirPath(item.ParentId) : root;
            if (!dirIndexMap.TryGetValue(dirPath, out var dirIndex))
            {
                dirIndex = dirs.Count;
                dirs.Add(dirPath);
                dirIndexMap[dirPath] = dirIndex;
            }
            // MFT 열거에는 크기/날짜가 없음 — 결과 표시 시점에 지연 조회
            items.Add(new Item(item.Name, dirIndex, -1, DateTime.MinValue));
        }
        return new FileNameIndex([.. items], [.. dirs]);
    }

    /// <summary>부분 문자열/글롭/path:/ext: 조합 검색. 대용량 인덱스는 병렬 스캔.</summary>
    public List<SearchHit> Search(string query, int maxResults = 2000, CancellationToken ct = default)
    {
        var parsed = new SearchQuery(query);
        if (parsed.IsEmpty) return [];

        var results = new ConcurrentBag<SearchHit>();
        var found = 0;

        var rangePartitioner = Partitioner.Create(0, _items.Length);
        Parallel.ForEach(rangePartitioner,
            new ParallelOptions { CancellationToken = ct },
            (range, state) =>
            {
                for (var i = range.Item1; i < range.Item2; i++)
                {
                    if (Volatile.Read(ref found) >= maxResults) { state.Stop(); return; }
                    var item = _items[i];
                    if (!parsed.Matches(item.Name, _dirPaths[item.DirIndex])) continue;
                    results.Add(new SearchHit(
                        Path.Combine(_dirPaths[item.DirIndex], item.Name),
                        item.Name, item.Size, item.ModifiedUtc));
                    Interlocked.Increment(ref found);
                }
            });

        return results
            .OrderBy(h => h.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }
}
