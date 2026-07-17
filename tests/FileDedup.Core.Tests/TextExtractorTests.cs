using System.IO.Compression;
using System.Text;
using FileDedup.Core.Extract;

namespace FileDedup.Core.Tests;

public class TextExtractorTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void UTF8_텍스트_추출()
    {
        var path = _dir.WriteFile("a.txt", "한국어 UTF-8 내용");
        Assert.Equal("한국어 UTF-8 내용", TextExtractorRegistry.ExtractText(path));
    }

    [Fact]
    public void CP949_레거시_인코딩_자동_감지()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949).GetBytes("한글 완성형 문서");
        var path = _dir.WriteFile("legacy.txt", cp949);

        Assert.Equal("한글 완성형 문서", TextExtractorRegistry.ExtractText(path));
    }

    [Fact]
    public void 바이너리_파일은_추출하지_않는다()
    {
        var path = _dir.WriteFile("bin.log", new byte[] { 0x00, 0x01, 0xFF, 0x00 });
        Assert.Null(TextExtractorRegistry.ExtractText(path));
    }

    [Fact]
    public void docx_본문_텍스트_추출()
    {
        var path = MakeZip("doc.docx", ("word/document.xml", """
            <?xml version="1.0"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:t>계약서 초안</w:t></w:r>
              <w:r><w:t>제2조 손해배상</w:t></w:r></w:p></w:body>
            </w:document>
            """));

        var text = TextExtractorRegistry.ExtractText(path);
        Assert.NotNull(text);
        Assert.Contains("계약서 초안", text);
        Assert.Contains("손해배상", text);
    }

    [Fact]
    public void xlsx_공유_문자열_추출()
    {
        var path = MakeZip("sheet.xlsx", ("xl/sharedStrings.xml", """
            <?xml version="1.0"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><t>증거목록</t></si><si><t>압수물번호</t></si>
            </sst>
            """));

        var text = TextExtractorRegistry.ExtractText(path);
        Assert.NotNull(text);
        Assert.Contains("증거목록", text);
    }

    [Fact]
    public void pptx_슬라이드_텍스트_추출()
    {
        var path = MakeZip("deck.pptx", ("ppt/slides/slide1.xml", """
            <?xml version="1.0"?>
            <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                   xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:t>수사 브리핑</a:t>
            </p:sld>
            """));

        var text = TextExtractorRegistry.ExtractText(path);
        Assert.NotNull(text);
        Assert.Contains("수사 브리핑", text);
    }

    [Fact]
    public void hwpx_섹션_텍스트_추출()
    {
        var path = MakeZip("doc.hwpx", ("Contents/section0.xml", """
            <?xml version="1.0"?>
            <hs:sec xmlns:hs="http://www.hancom.co.kr/hwpml/2011/section"
                    xmlns:hp="http://www.hancom.co.kr/hwpml/2011/paragraph">
              <hp:p><hp:run><hp:t>한글 문서 본문입니다</hp:t></hp:run></hp:p>
            </hs:sec>
            """));

        var text = TextExtractorRegistry.ExtractText(path);
        Assert.NotNull(text);
        Assert.Contains("한글 문서 본문입니다", text);
    }

    [Fact]
    public void hwp_본문_텍스트_추출()
    {
        // HWP 5.0 구조를 그대로 갖춘 최소 문서를 만들어 파서를 검증한다
        var path = Path.Combine(_dir.Path, "doc.hwp");
        using (var root = OpenMcdf.RootStorage.Create(path))
        {
            using (var header = root.CreateStream("FileHeader"))
            {
                var bytes = new byte[256];
                Encoding.ASCII.GetBytes("HWP Document File").CopyTo(bytes, 0);
                bytes[36] = 0x01; // bit0 = 압축 사용
                header.Write(bytes, 0, bytes.Length);
            }

            var body = root.CreateStorage("BodyText");
            using var section = body.CreateStream("Section0");

            var textBytes = Encoding.Unicode.GetBytes("한글 본문 추출 테스트");
            var record = new byte[4 + textBytes.Length];
            // 레코드 헤더: tag=HWPTAG_PARA_TEXT(0x43), level=0, size
            var recordHeader = 0x43u | ((uint)textBytes.Length << 20);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(record, recordHeader);
            textBytes.CopyTo(record, 4);

            using var ms = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(
                       ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(record);
            section.Write(ms.ToArray(), 0, (int)ms.Length);
        }

        var extracted = TextExtractorRegistry.ExtractText(path);
        Assert.NotNull(extracted);
        Assert.Contains("한글 본문 추출 테스트", extracted);
    }

    [Fact]
    public void 손상된_문서는_null을_반환하고_예외를_던지지_않는다()
    {
        var path = _dir.WriteFile("broken.docx", "이것은 zip이 아님");
        Assert.Null(TextExtractorRegistry.ExtractText(path));

        var hwp = _dir.WriteFile("broken.hwp", new byte[] { 1, 2, 3 });
        Assert.Null(TextExtractorRegistry.ExtractText(hwp));
    }

    [Fact]
    public void 지원하지_않는_확장자는_null()
    {
        var path = _dir.WriteFile("img.jpg", new byte[] { 0xFF, 0xD8 });
        Assert.Null(TextExtractorRegistry.ExtractText(path));
        Assert.False(TextExtractorRegistry.IsSupported(path));
    }

    private string MakeZip(string fileName, params (string EntryName, string Xml)[] entries)
    {
        var path = Path.Combine(_dir.Path, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, xml) in entries)
        {
            var entry = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(xml.TrimStart());
        }
        return path;
    }
}
