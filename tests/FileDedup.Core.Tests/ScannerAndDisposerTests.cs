using FileDedup.Core.Dedup;
using FileDedup.Core.Delete;
using FileDedup.Core.Scanning;

namespace FileDedup.Core.Tests;

public class ScannerAndDisposerTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void 스캐너는_제외_패턴과_크기_필터를_적용한다()
    {
        _dir.WriteFile("keep.txt", "12345");
        _dir.WriteFile("skip.tmp", "12345");
        _dir.WriteFile("too-small.txt", "1");
        _dir.WriteFile("sub/nested.txt", "12345");

        var files = FolderScanner.Scan([_dir.Path], new ScanOptions
        {
            ExcludePatterns = ["*.tmp"],
            MinFileSize = 2,
        });

        var names = files.Select(f => f.Name).Order().ToArray();
        Assert.Equal(["keep.txt", "nested.txt"], names);
    }

    [Fact]
    public void 하위폴더_제외_옵션이_동작한다()
    {
        _dir.WriteFile("top.txt", "abc");
        _dir.WriteFile("sub/nested.txt", "abc");

        var files = FolderScanner.Scan([_dir.Path],
            new ScanOptions { IncludeSubdirectories = false });

        Assert.Equal("top.txt", Assert.Single(files).Name);
    }

    [Fact]
    public async Task 전체_흐름_스캔_판별_이동_보고서()
    {
        _dir.WriteFile("docs/report.pdf", "중복 문서 내용");
        _dir.WriteFile("backup/report.pdf", "중복 문서 내용");
        var quarantine = Path.Combine(_dir.Path, "quarantine");

        var files = FolderScanner.Scan(
            [Path.Combine(_dir.Path, "docs"), Path.Combine(_dir.Path, "backup")],
            new ScanOptions());
        var result = await DuplicateFinder.FindAsync(files,
            new DedupOptions { Criteria = DuplicateCriteria.Sha256 });
        var group = Assert.Single(result.Groups);

        var toRemove = SelectionRules.Apply(result.Groups, KeepPolicy.PreferFolder,
            Path.Combine(_dir.Path, "docs"));
        var removed = Assert.Single(toRemove);
        Assert.Contains("backup", removed.FullPath);

        var disposer = new FileDisposer(new UnsupportedRecycleBin());
        var outcomes = disposer.Dispose(toRemove, DisposeMethod.MoveTo, quarantine);
        Assert.All(outcomes, o => Assert.True(o.Success));
        Assert.False(File.Exists(removed.FullPath));
        Assert.True(File.Exists(Path.Combine(quarantine, "report.pdf")));

        var reportPath = Path.Combine(_dir.Path, "report.csv");
        CsvReportWriter.Write(reportPath, result.Groups,
            outcomes.ToDictionary(o => o.File));
        var csv = File.ReadAllText(reportPath);
        Assert.Contains("SHA256", csv);
        Assert.Contains("성공", csv);
        Assert.Contains("보존", csv);
        Assert.Equal(3, csv.TrimEnd().Split('\n').Length); // 헤더 + 파일 2건
    }

    [Fact]
    public void 이동_시_이름_충돌은_번호를_붙여_회피한다()
    {
        var src1 = _dir.WriteFile("a/f.txt", "1");
        var src2 = _dir.WriteFile("b/f.txt", "2");
        var target = Path.Combine(_dir.Path, "moved");

        var disposer = new FileDisposer(new UnsupportedRecycleBin());
        var records = new[] { src1, src2 }.Select(p => new FileRecord
        {
            Entry = new FileEntry(p, Path.GetFileName(p), 1, DateTime.UtcNow, DateTime.UtcNow),
        });
        var outcomes = disposer.Dispose(records, DisposeMethod.MoveTo, target);

        Assert.All(outcomes, o => Assert.True(o.Success));
        Assert.True(File.Exists(Path.Combine(target, "f.txt")));
        Assert.True(File.Exists(Path.Combine(target, "f (1).txt")));
    }

    [Fact]
    public void 휴지통_미지원_플랫폼에서_휴지통_방식은_예외()
    {
        var disposer = new FileDisposer(new UnsupportedRecycleBin());
        Assert.Throws<PlatformNotSupportedException>(
            () => disposer.Dispose([], DisposeMethod.RecycleBin));
    }
}
