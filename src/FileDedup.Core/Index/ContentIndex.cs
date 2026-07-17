using FileDedup.Core.Extract;
using FileDedup.Core.Scanning;
using Microsoft.Data.Sqlite;

namespace FileDedup.Core.Index;

public sealed record ContentHit(string Path, string Snippet);

public sealed record ContentIndexProgress(int Processed, int Total, string CurrentPath);

public sealed record ContentIndexStats(int Indexed, int Skipped, int Removed, int Failed);

/// <summary>
/// 파일 내용 전문검색 인덱스 (SQLite FTS5 + trigram 토크나이저).
/// trigram은 한국어를 포함한 부분 문자열 매칭을 지원한다.
/// 크기+수정일 변경 감지로 증분 인덱싱한다.
/// </summary>
public sealed class ContentIndex : IDisposable
{
    private readonly SqliteConnection _conn;

    public ContentIndex(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        Execute("""
            CREATE TABLE IF NOT EXISTS docs(
                id INTEGER PRIMARY KEY,
                path TEXT UNIQUE NOT NULL,
                size INTEGER NOT NULL,
                modified INTEGER NOT NULL);
            """);
        Execute("""
            CREATE VIRTUAL TABLE IF NOT EXISTS docs_fts
            USING fts5(content, tokenize='trigram');
            """);
        Execute("PRAGMA journal_mode=WAL;");
    }

    /// <summary>지정 폴더들의 지원 형식 파일을 인덱싱한다 (변경 없는 파일은 건너뜀).</summary>
    public async Task<ContentIndexStats> IndexAsync(
        IEnumerable<string> roots,
        ScanOptions? scanOptions = null,
        IProgress<ContentIndexProgress>? progress = null,
        CancellationToken ct = default)
    {
        var files = await FolderScanner.ScanAsync(roots, scanOptions ?? new ScanOptions(), ct: ct);
        var candidates = files.Where(f => TextExtractorRegistry.IsSupported(f.FullPath)).ToList();
        var currentPaths = new HashSet<string>(candidates.Select(f => f.FullPath));

        int indexed = 0, skipped = 0, failed = 0;
        var processed = 0;

        foreach (var file in candidates)
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            if (processed % 20 == 0)
                progress?.Report(new ContentIndexProgress(processed, candidates.Count, file.FullPath));

            var existing = QueryDoc(file.FullPath);
            if (existing is not null
                && existing.Value.Size == file.Length
                && existing.Value.Modified == file.ModifiedUtc.Ticks)
            {
                skipped++;
                continue;
            }

            // 추출은 트랜잭션 밖에서 (오래 걸릴 수 있음)
            var text = await Task.Run(() => TextExtractorRegistry.ExtractText(file.FullPath), ct);
            if (text is null) { failed++; continue; }

            using var tx = _conn.BeginTransaction();
            var id = UpsertDoc(file, tx);
            Execute("DELETE FROM docs_fts WHERE rowid = $id;", tx, ("$id", id));
            Execute("INSERT INTO docs_fts(rowid, content) VALUES($id, $content);", tx,
                ("$id", id), ("$content", text));
            tx.Commit();
            indexed++;
        }

        var removed = RemoveMissing(roots, currentPaths);
        progress?.Report(new ContentIndexProgress(candidates.Count, candidates.Count, string.Empty));
        return new ContentIndexStats(indexed, skipped, removed, failed);
    }

    /// <summary>전문 검색. 3글자 이상 용어는 FTS5 MATCH, 짧은 용어는 LIKE 폴백.</summary>
    public List<ContentHit> Search(string query, int maxResults = 200)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return [];

        var results = new List<ContentHit>();
        using var cmd = _conn.CreateCommand();

        if (terms.All(t => t.Length >= 3))
        {
            // trigram MATCH: 각 용어를 따옴표로 감싸 구문 그대로 매칭
            var match = string.Join(" AND ",
                terms.Select(t => $"\"{t.Replace("\"", "\"\"")}\""));
            cmd.CommandText = """
                SELECT d.path, snippet(docs_fts, 0, '[', ']', '…', 12)
                FROM docs_fts JOIN docs d ON d.id = docs_fts.rowid
                WHERE docs_fts MATCH $match
                ORDER BY rank LIMIT $max;
                """;
            cmd.Parameters.AddWithValue("$match", match);
        }
        else
        {
            var conditions = new List<string>();
            for (var i = 0; i < terms.Length; i++)
            {
                conditions.Add($"docs_fts.content LIKE $like{i} ESCAPE '\\'");
                var escaped = terms[i].Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                cmd.Parameters.AddWithValue($"$like{i}", $"%{escaped}%");
            }
            cmd.CommandText = $"""
                SELECT d.path, substr(docs_fts.content, 1, 80)
                FROM docs_fts JOIN docs d ON d.id = docs_fts.rowid
                WHERE {string.Join(" AND ", conditions)}
                LIMIT $max;
                """;
        }
        cmd.Parameters.AddWithValue("$max", maxResults);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new ContentHit(reader.GetString(0), reader.GetString(1)));
        return results;
    }

    public int DocumentCount
    {
        get
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM docs;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    private (long Size, long Modified)? QueryDoc(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT size, modified FROM docs WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetInt64(1)) : null;
    }

    private long UpsertDoc(FileEntry file, SqliteTransaction tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO docs(path, size, modified) VALUES($path, $size, $modified)
            ON CONFLICT(path) DO UPDATE SET size = $size, modified = $modified
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$path", file.FullPath);
        cmd.Parameters.AddWithValue("$size", file.Length);
        cmd.Parameters.AddWithValue("$modified", file.ModifiedUtc.Ticks);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>인덱싱 대상 폴더 아래에 있었지만 이번 스캔에서 사라진 문서를 제거.</summary>
    private int RemoveMissing(IEnumerable<string> roots, HashSet<string> currentPaths)
    {
        var stale = new List<long>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, path FROM docs;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(1);
                var underRoot = roots.Any(r => path.StartsWith(
                    Path.TrimEndingDirectorySeparator(r) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                if (underRoot && !currentPaths.Contains(path))
                    stale.Add(reader.GetInt64(0));
            }
        }
        foreach (var id in stale)
        {
            Execute("DELETE FROM docs_fts WHERE rowid = $id;", null, ("$id", id));
            Execute("DELETE FROM docs WHERE id = $id;", null, ("$id", id));
        }
        return stale.Count;
    }

    private void Execute(string sql, SqliteTransaction? tx = null,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}
