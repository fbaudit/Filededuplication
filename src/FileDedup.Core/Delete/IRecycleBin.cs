namespace FileDedup.Core.Delete;

/// <summary>휴지통 이동 추상화. Windows 구현은 FileDedup.Windows에서 주입한다.</summary>
public interface IRecycleBin
{
    bool IsSupported { get; }
    void Send(string path);
}

/// <summary>휴지통을 지원하지 않는 플랫폼용 (호출 시 예외).</summary>
public sealed class UnsupportedRecycleBin : IRecycleBin
{
    public bool IsSupported => false;
    public void Send(string path)
        => throw new PlatformNotSupportedException("이 플랫폼에서는 휴지통 이동을 지원하지 않습니다.");
}
