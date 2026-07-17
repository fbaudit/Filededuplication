using System.IO.Compression;
using System.Text;
using System.Xml;

namespace FileDedup.Core.Extract;

/// <summary>
/// MS Office OOXML(docx/xlsx/pptx). zip 내부 XML에서 텍스트 노드(t 요소)만 수집한다.
/// </summary>
public sealed class OoxmlExtractor : ITextExtractor
{
    public IReadOnlyList<string> Extensions { get; } = [".docx", ".xlsx", ".pptx"];

    public string? Extract(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var sb = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            if (!IsTextPart(entry.FullName)) continue;
            using var stream = entry.Open();
            AppendTextNodes(stream, sb);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static bool IsTextPart(string entryName) =>
        entryName is "word/document.xml" or "xl/sharedStrings.xml"
        || entryName.StartsWith("word/header", StringComparison.Ordinal)
        || entryName.StartsWith("word/footer", StringComparison.Ordinal)
        || (entryName.StartsWith("ppt/slides/slide", StringComparison.Ordinal)
            && entryName.EndsWith(".xml", StringComparison.Ordinal))
        || (entryName.StartsWith("ppt/notesSlides/notesSlide", StringComparison.Ordinal)
            && entryName.EndsWith(".xml", StringComparison.Ordinal));

    private static void AppendTextNodes(Stream xmlStream, StringBuilder sb)
    {
        using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true,
        });
        while (reader.Read())
        {
            // w:t (Word), a:t (PowerPoint), t (Excel sharedStrings)
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
            {
                var text = reader.ReadElementContentAsString();
                if (text.Length > 0)
                {
                    sb.Append(text);
                    sb.Append(' ');
                }
            }
        }
    }
}
