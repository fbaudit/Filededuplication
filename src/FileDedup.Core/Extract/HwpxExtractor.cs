using System.IO.Compression;
using System.Text;
using System.Xml;

namespace FileDedup.Core.Extract;

/// <summary>한글 HWPX(OWPML). zip 내부 Contents/section*.xml의 텍스트 노드를 수집한다.</summary>
public sealed class HwpxExtractor : ITextExtractor
{
    public IReadOnlyList<string> Extensions { get; } = [".hwpx"];

    public string? Extract(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var sb = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase)
                || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
            });
            while (reader.Read())
            {
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
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
