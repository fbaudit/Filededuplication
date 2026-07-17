using System.Text.RegularExpressions;

namespace FileDedup.Core.Scanning;

/// <summary>글롭 패턴(*, ?)을 대소문자 무시 정규식으로 변환해 매칭한다.</summary>
public sealed class GlobMatcher
{
    private readonly Regex[] _regexes;

    public GlobMatcher(IEnumerable<string> patterns)
    {
        _regexes = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(ToRegex)
            .ToArray();
    }

    public bool IsEmpty => _regexes.Length == 0;

    /// <summary>파일명 또는 전체 경로 중 하나라도 패턴에 걸리면 true.</summary>
    public bool IsMatch(string fullPath, string fileName)
        => _regexes.Any(r => r.IsMatch(fileName) || r.IsMatch(fullPath));

    private static Regex ToRegex(string glob)
    {
        var escaped = Regex.Escape(glob.Trim())
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        return new Regex($"^{escaped}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
