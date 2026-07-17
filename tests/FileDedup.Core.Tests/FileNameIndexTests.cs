using FileDedup.Core.Scanning;
using FileDedup.Core.Search;

namespace FileDedup.Core.Tests;

public class FileNameIndexTests
{
    private static FileEntry Entry(string path)
        => new(path, Path.GetFileName(path), 10, DateTime.UtcNow, DateTime.UtcNow);

    private static readonly FileNameIndex Index = FileNameIndex.FromEntries(
    [
        Entry("/data/reports/2024년_감정보고서.hwp"),
        Entry("/data/reports/2025년_감정보고서.hwp"),
        Entry("/data/evidence/photo_001.jpg"),
        Entry("/data/evidence/photo_002.PNG"),
        Entry("/backup/old_report.pdf"),
    ]);

    [Fact]
    public void 부분_문자열_검색은_대소문자를_무시한다()
    {
        var hits = Index.Search("PHOTO");
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void 한국어_부분_문자열_검색()
    {
        var hits = Index.Search("감정보고서");
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.EndsWith(".hwp", h.FullPath));
    }

    [Fact]
    public void 공백은_AND_조건이다()
    {
        var hits = Index.Search("감정 2024");
        Assert.Single(hits);
        Assert.Contains("2024", hits[0].Name);
    }

    [Fact]
    public void 와일드카드_검색()
    {
        var hits = Index.Search("photo_*.jpg");
        Assert.Single(hits);
        Assert.Equal("photo_001.jpg", hits[0].Name);
    }

    [Fact]
    public void ext_필터는_확장자만_매칭한다()
    {
        var hits = Index.Search("ext:png");
        Assert.Single(hits);
        Assert.Equal("photo_002.PNG", hits[0].Name);
    }

    [Fact]
    public void path_필터는_디렉터리_경로에_매칭한다()
    {
        var hits = Index.Search("path:evidence");
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void 빈_검색어는_빈_결과()
    {
        Assert.Empty(Index.Search("   "));
    }

    [Fact]
    public void MFT_열거_결과에서_부모_체인으로_전체_경로를_조립한다()
    {
        // 가상 볼륨: root(5) ← dirA(100) ← dirB(200), 파일들
        List<VolumeItem> volume =
        [
            new(100, 5, "dirA", true),
            new(200, 100, "dirB", true),
            new(1000, 200, "deep.txt", false),
            new(1001, 100, "mid.txt", false),
            new(1002, 5, "top.txt", false),
        ];
        var root = Path.Combine(Path.GetTempPath(), "vol");
        var index = FileNameIndex.FromVolumeItems(root + Path.DirectorySeparatorChar, volume);

        Assert.Equal(3, index.Count);
        var deep = Assert.Single(index.Search("deep"));
        Assert.Equal(Path.Combine(root, "dirA", "dirB", "deep.txt"), deep.FullPath);
        var top = Assert.Single(index.Search("top"));
        Assert.Equal(Path.Combine(root, "top.txt"), top.FullPath);
    }

    [Fact]
    public void 대용량_인덱스에서도_최대_결과_수를_지킨다()
    {
        var entries = Enumerable.Range(0, 50_000)
            .Select(i => Entry($"/bulk/file_{i:D6}.dat"))
            .ToList();
        var index = FileNameIndex.FromEntries(entries);

        var hits = index.Search("file_", maxResults: 100);
        Assert.Equal(100, hits.Count);
    }
}
