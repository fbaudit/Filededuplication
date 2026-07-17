namespace FileDedup.Core.Scanning;

public sealed class ScanOptions
{
    /// <summary>하위 폴더 포함 여부.</summary>
    public bool IncludeSubdirectories { get; init; } = true;

    /// <summary>제외할 글롭 패턴 (파일명 또는 전체 경로에 매칭, 예: *.tmp, *\node_modules\*).</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = [];

    public long MinFileSize { get; init; }

    public long MaxFileSize { get; init; } = long.MaxValue;

    /// <summary>숨김/시스템 파일 제외 여부.</summary>
    public bool SkipHiddenAndSystem { get; init; } = true;
}
