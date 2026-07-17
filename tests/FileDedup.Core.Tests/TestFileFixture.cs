namespace FileDedup.Core.Tests;

/// <summary>테스트마다 임시 디렉터리를 만들고 끝나면 정리한다.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } =
        Directory.CreateTempSubdirectory("filededup-test-").FullName;

    public string WriteFile(string relativePath, byte[] content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public string WriteFile(string relativePath, string content)
        => WriteFile(relativePath, System.Text.Encoding.UTF8.GetBytes(content));

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* 테스트 정리 실패 무시 */ }
    }
}
