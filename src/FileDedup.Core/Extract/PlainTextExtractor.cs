using System.Text;

namespace FileDedup.Core.Extract;

/// <summary>일반 텍스트 파일. BOM → UTF-8 검증 → CP949 순으로 인코딩을 감지한다.</summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    static PlainTextExtractor()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public IReadOnlyList<string> Extensions { get; } =
    [
        ".txt", ".log", ".csv", ".md", ".json", ".xml", ".html", ".htm", ".ini",
        ".cs", ".py", ".js", ".ts", ".java", ".c", ".cpp", ".h", ".sql", ".ps1", ".bat",
    ];

    private const int MaxBytes = 16 * 1024 * 1024;

    public string? Extract(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return string.Empty;
        // NUL 바이트가 있으면 바이너리로 간주 (UTF-16 BOM 파일 제외)
        var hasBom = bytes.Length >= 2 &&
                     ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
        if (!hasBom && Array.IndexOf(bytes, (byte)0) >= 0) return null;

        if (hasBom || (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF))
        {
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(949).GetString(bytes); // 한국어 레거시(EUC-KR/CP949)
        }
    }
}
