using FileDedup.Core.Hashing;

namespace FileDedup.Core.Dedup;

/// <summary>중복 판별 기준. 선택된 기준을 모두 만족해야 중복으로 본다 (DoubleKiller 방식).</summary>
[Flags]
public enum DuplicateCriteria
{
    None = 0,
    FileName = 1,
    Size = 2,
    CreatedDate = 4,
    ModifiedDate = 8,
    Crc32 = 16,
    Md5 = 32,
    Sha256 = 64,
}

public static class DuplicateCriteriaExtensions
{
    public static bool RequiresContentHash(this DuplicateCriteria c)
        => (c & (DuplicateCriteria.Crc32 | DuplicateCriteria.Md5 | DuplicateCriteria.Sha256)) != 0;

    public static HashAlgorithms ToHashAlgorithms(this DuplicateCriteria c)
    {
        var algs = HashAlgorithms.None;
        if (c.HasFlag(DuplicateCriteria.Crc32)) algs |= HashAlgorithms.Crc32;
        if (c.HasFlag(DuplicateCriteria.Md5)) algs |= HashAlgorithms.Md5;
        if (c.HasFlag(DuplicateCriteria.Sha256)) algs |= HashAlgorithms.Sha256;
        return algs;
    }
}

public sealed class DedupOptions
{
    public DuplicateCriteria Criteria { get; init; } =
        DuplicateCriteria.Size | DuplicateCriteria.Sha256;

    /// <summary>파일명 비교 시 대소문자 무시.</summary>
    public bool IgnoreCase { get; init; } = true;

    /// <summary>날짜 비교 허용 오차(초). FAT/NTFS 타임스탬프 정밀도 차이 흡수용.</summary>
    public int TimestampToleranceSeconds { get; init; } = 2;

    /// <summary>해시 병렬 계산 스레드 수 (0 = 자동).</summary>
    public int DegreeOfParallelism { get; init; }
}
