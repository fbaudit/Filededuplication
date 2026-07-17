namespace FileDedup.Core.Search;

/// <summary>MFT 열거 결과 1건. Id/ParentId는 NTFS 파일 참조 번호(FRN).</summary>
public sealed record VolumeItem(ulong Id, ulong ParentId, string Name, bool IsDirectory);

/// <summary>
/// 볼륨 전체 파일 목록의 고속 열거 추상화.
/// Windows 구현(FileDedup.Windows.MftEnumerator)은 NTFS MFT를 직접 읽는다 (Everything 방식).
/// </summary>
public interface IVolumeEnumerator
{
    /// <summary>현재 환경에서 사용 가능한지 (Windows + NTFS + 관리자 권한).</summary>
    bool IsSupported(string driveRoot);

    /// <summary>볼륨의 전체 파일/디렉터리 항목을 열거한다.</summary>
    List<VolumeItem> Enumerate(string driveRoot, CancellationToken ct = default);
}
