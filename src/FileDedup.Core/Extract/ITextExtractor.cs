namespace FileDedup.Core.Extract;

public interface ITextExtractor
{
    IReadOnlyList<string> Extensions { get; }

    /// <summary>파일에서 본문 텍스트를 추출한다. 지원 불가/파싱 실패 시 null.</summary>
    string? Extract(string path);
}

/// <summary>확장자별 추출기 라우팅. 실패는 조용히 null 처리해 인덱싱을 계속한다.</summary>
public static class TextExtractorRegistry
{
    /// <summary>인덱싱할 텍스트 최대 길이 (문자 수). 초과분은 절단.</summary>
    public const int MaxTextLength = 4_000_000;

    private static readonly Dictionary<string, ITextExtractor> ByExtension = Build();

    private static Dictionary<string, ITextExtractor> Build()
    {
        var map = new Dictionary<string, ITextExtractor>(StringComparer.OrdinalIgnoreCase);
        ITextExtractor[] extractors =
        [
            new PlainTextExtractor(),
            new OoxmlExtractor(),
            new PdfExtractor(),
            new HwpExtractor(),
            new HwpxExtractor(),
        ];
        foreach (var extractor in extractors)
            foreach (var ext in extractor.Extensions)
                map[ext] = extractor;
        return map;
    }

    public static IReadOnlyCollection<string> SupportedExtensions => ByExtension.Keys;

    public static bool IsSupported(string path)
        => ByExtension.ContainsKey(Path.GetExtension(path));

    public static string? ExtractText(string path)
    {
        if (!ByExtension.TryGetValue(Path.GetExtension(path), out var extractor))
            return null;
        try
        {
            var text = extractor.Extract(path);
            return text is { Length: > MaxTextLength } ? text[..MaxTextLength] : text;
        }
        catch
        {
            return null; // 손상 파일 등은 건너뛰고 인덱싱 계속
        }
    }
}
