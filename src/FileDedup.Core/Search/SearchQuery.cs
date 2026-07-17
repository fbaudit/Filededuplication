using System.Text.RegularExpressions;

namespace FileDedup.Core.Search;

/// <summary>
/// Everything식 검색 문법 파서.
/// 공백 = AND, '*'/'?' 와일드카드, "path:용어"는 경로에, "ext:pdf"는 확장자에 매칭.
/// </summary>
public sealed class SearchQuery
{
    private readonly List<Regex> _nameGlobs = [];
    private readonly List<string> _nameTerms = [];
    private readonly List<string> _pathTerms = [];
    private readonly List<string> _extensions = [];

    public bool IsEmpty { get; }

    public SearchQuery(string query)
    {
        foreach (var raw in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            {
                var term = raw[5..];
                if (term.Length > 0) _pathTerms.Add(term);
            }
            else if (raw.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                var ext = raw[4..].TrimStart('.');
                if (ext.Length > 0) _extensions.Add("." + ext);
            }
            else if (raw.Contains('*') || raw.Contains('?'))
            {
                var pattern = Regex.Escape(raw).Replace(@"\*", ".*").Replace(@"\?", ".");
                _nameGlobs.Add(new Regex($"^{pattern}$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            else
            {
                _nameTerms.Add(raw);
            }
        }
        IsEmpty = _nameGlobs.Count == 0 && _nameTerms.Count == 0
                  && _pathTerms.Count == 0 && _extensions.Count == 0;
    }

    public bool Matches(string name, string directoryPath)
    {
        foreach (var term in _nameTerms)
            if (!name.Contains(term, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var glob in _nameGlobs)
            if (!glob.IsMatch(name)) return false;
        foreach (var term in _pathTerms)
            if (!directoryPath.Contains(term, StringComparison.OrdinalIgnoreCase)) return false;
        if (_extensions.Count > 0)
        {
            var ext = Path.GetExtension(name);
            if (!_extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }
}
