using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenMcdf;

namespace FileDedup.Core.Extract;

/// <summary>
/// 한글 HWP 5.0 본문 텍스트 추출기 (경량 자체 파서).
/// HWP 5.0 = OLE 복합 파일. BodyText/Section* 스트림을 (압축 시 raw deflate 해제 후)
/// 레코드 단위로 파싱해 HWPTAG_PARA_TEXT의 UTF-16 텍스트만 수집한다.
/// 배포용(DRM) 문서·구버전(HWP 3.x)은 지원하지 않는다.
/// </summary>
public sealed class HwpExtractor : ITextExtractor
{
    public IReadOnlyList<string> Extensions { get; } = [".hwp"];

    private const int HwpTagParaText = 0x43; // HWPTAG_BEGIN(0x10) + 51

    public string? Extract(string path)
    {
        using var root = RootStorage.OpenRead(path);

        bool compressed;
        using (var header = root.OpenStream("FileHeader"))
        {
            var headerBytes = new byte[256];
            header.ReadExactly(headerBytes, 0, Math.Min(256, (int)header.Length));
            var signature = Encoding.ASCII.GetString(headerBytes, 0, 17);
            if (signature != "HWP Document File") return null;
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.AsSpan(36));
            if ((flags & 0x2) != 0) return null; // 암호화 문서
            compressed = (flags & 0x1) != 0;
        }

        if (!root.TryOpenStorage("BodyText", out var bodyText) || bodyText is null)
            return null;

        var sb = new StringBuilder();
        for (var i = 0; ; i++)
        {
            if (!bodyText.TryOpenStream($"Section{i}", out var stream) || stream is null)
                break;
            byte[] section;
            using (stream)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                section = ms.ToArray();
            }

            if (compressed)
            {
                using var inflate = new DeflateStream(
                    new MemoryStream(section), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                inflate.CopyTo(ms);
                section = ms.ToArray();
            }
            AppendSectionText(section, sb);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static void AppendSectionText(byte[] data, StringBuilder sb)
    {
        var offset = 0;
        while (offset + 4 <= data.Length)
        {
            var header = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            offset += 4;
            var tag = (int)(header & 0x3FF);
            var size = (int)(header >> 20);
            if (size == 0xFFF) // 확장 크기: 다음 4바이트가 실제 크기
            {
                if (offset + 4 > data.Length) break;
                size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
                offset += 4;
            }
            if (size < 0 || offset + size > data.Length) break;

            if (tag == HwpTagParaText)
            {
                AppendParaText(data.AsSpan(offset, size), sb);
                sb.Append('\n');
            }
            offset += size;
        }
    }

    private static void AppendParaText(ReadOnlySpan<byte> para, StringBuilder sb)
    {
        for (var i = 0; i + 2 <= para.Length; i += 2)
        {
            var ch = BinaryPrimitives.ReadUInt16LittleEndian(para[i..]);
            if (ch >= 32)
            {
                sb.Append((char)ch);
            }
            else if (ch is 10 or 13)
            {
                sb.Append('\n');
            }
            else if (ch is >= 1 and <= 23 and not 10 and not 13)
            {
                // 인라인/확장 컨트롤은 8워드(16바이트) 차지 — 나머지 7워드 건너뜀
                i += 14;
            }
            // 0, 24~31: 예약 문자 1워드 — 무시
        }
    }
}
