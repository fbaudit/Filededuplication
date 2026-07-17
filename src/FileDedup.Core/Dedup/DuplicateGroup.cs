using FileDedup.Core.Hashing;
using FileDedup.Core.Scanning;

namespace FileDedup.Core.Dedup;

/// <summary>중복 판별이 끝난 파일 1건 (계산된 해시 포함).</summary>
public sealed class FileRecord
{
    public required FileEntry Entry { get; init; }
    public FileHashes Hashes { get; set; } = new(null, null, null);

    public string FullPath => Entry.FullPath;
    public long Length => Entry.Length;
}

/// <summary>서로 중복인 파일들의 그룹.</summary>
public sealed class DuplicateGroup
{
    public required IReadOnlyList<FileRecord> Files { get; init; }

    /// <summary>그룹당 1개를 남기고 지웠을 때 절약되는 바이트.</summary>
    public long WastedBytes => Files.Skip(1).Sum(f => f.Length);
}

public enum DedupStage { Scanning, Grouping, PartialHashing, FullHashing, Done }

public sealed record DedupProgress(
    DedupStage Stage, int ProcessedFiles, int TotalFiles, long ProcessedBytes);
