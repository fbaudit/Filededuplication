using System.Text;
using FileDedup.Core.Dedup;

namespace FileDedup.Core.Delete;

/// <summary>
/// 포렌식 감사용 CSV 보고서. 처리 대상 전체의 경로·크기·날짜·해시·처리 결과를 기록한다.
/// Excel 한글 호환을 위해 UTF-8 BOM으로 저장.
/// </summary>
public static class CsvReportWriter
{
    public static void Write(
        string reportPath,
        IReadOnlyList<DuplicateGroup> groups,
        IReadOnlyDictionary<FileRecord, DisposeOutcome>? outcomes = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("그룹ID,파일경로,크기(바이트),만든일자(UTC),수정일자(UTC),CRC32,MD5,SHA256,처리,결과,오류");

        for (var groupId = 0; groupId < groups.Count; groupId++)
        {
            foreach (var file in groups[groupId].Files)
            {
                DisposeOutcome? outcome = null;
                outcomes?.TryGetValue(file, out outcome);
                sb.AppendLine(string.Join(",",
                    (groupId + 1).ToString(),
                    Escape(file.FullPath),
                    file.Length.ToString(),
                    file.Entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    file.Entry.ModifiedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    file.Hashes.Crc32 ?? "",
                    file.Hashes.Md5 ?? "",
                    file.Hashes.Sha256 ?? "",
                    Escape(outcome?.Action ?? "보존"),
                    outcome is null ? "" : outcome.Success ? "성공" : "실패",
                    Escape(outcome?.Error ?? "")));
            }
        }

        File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
        => value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
