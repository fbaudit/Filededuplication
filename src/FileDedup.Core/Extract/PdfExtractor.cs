using System.Text;
using UglyToad.PdfPig;

namespace FileDedup.Core.Extract;

/// <summary>PDF 텍스트 레이어 추출 (스캔 이미지 PDF의 OCR은 지원하지 않음).</summary>
public sealed class PdfExtractor : ITextExtractor
{
    public IReadOnlyList<string> Extensions { get; } = [".pdf"];

    public string? Extract(string path)
    {
        var sb = new StringBuilder();
        using (var document = PdfDocument.Open(path))
        {
            foreach (var page in document.GetPages())
            {
                sb.Append(page.Text);
                sb.Append('\n');
                if (sb.Length > TextExtractorRegistry.MaxTextLength) break;
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
