namespace FileDedup.Core.Scanning;

/// <summary>스캔된 파일 1건의 메타데이터.</summary>
public sealed record FileEntry(
    string FullPath,
    string Name,
    long Length,
    DateTime CreatedUtc,
    DateTime ModifiedUtc);
