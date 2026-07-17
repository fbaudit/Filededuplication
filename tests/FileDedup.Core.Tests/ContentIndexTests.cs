using FileDedup.Core.Index;

namespace FileDedup.Core.Tests;

public class ContentIndexTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ContentIndex _index;

    public ContentIndexTests()
        => _index = new ContentIndex(Path.Combine(_dir.Path, "index.db"));

    public void Dispose()
    {
        _index.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public async Task 인덱싱_후_한국어_내용_검색이_된다()
    {
        var docs = Path.Combine(_dir.Path, "docs");
        _dir.WriteFile("docs/사건기록.txt", "피의자 진술 조서 내용입니다. 압수수색 영장 발부.");
        _dir.WriteFile("docs/기타메모.txt", "관련 없는 일반 메모");

        var stats = await _index.IndexAsync([docs]);
        Assert.Equal(2, stats.Indexed);

        var hits = _index.Search("압수수색");
        var hit = Assert.Single(hits);
        Assert.EndsWith("사건기록.txt", hit.Path);
        Assert.Contains("압수수색", hit.Snippet);
    }

    [Fact]
    public async Task 여러_용어는_AND_조건이다()
    {
        var docs = Path.Combine(_dir.Path, "docs");
        _dir.WriteFile("docs/a.txt", "디지털 포렌식 분석 결과");
        _dir.WriteFile("docs/b.txt", "디지털 카메라 사용법");

        await _index.IndexAsync([docs]);

        Assert.Single(_index.Search("디지털 포렌식"));
        Assert.Equal(2, _index.Search("디지털").Count);
    }

    [Fact]
    public async Task 변경_없는_파일은_재인덱싱을_건너뛴다()
    {
        var docs = Path.Combine(_dir.Path, "docs");
        _dir.WriteFile("docs/a.txt", "내용");

        var first = await _index.IndexAsync([docs]);
        Assert.Equal(1, first.Indexed);

        var second = await _index.IndexAsync([docs]);
        Assert.Equal(0, second.Indexed);
        Assert.Equal(1, second.Skipped);
    }

    [Fact]
    public async Task 수정된_파일은_재인덱싱되고_새_내용으로_검색된다()
    {
        var docs = Path.Combine(_dir.Path, "docs");
        var file = _dir.WriteFile("docs/a.txt", "원래 내용 알파벳");
        await _index.IndexAsync([docs]);

        File.WriteAllText(file, "수정된 내용 베타버전");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        var stats = await _index.IndexAsync([docs]);

        Assert.Equal(1, stats.Indexed);
        Assert.Empty(_index.Search("알파벳"));
        Assert.Single(_index.Search("베타버전"));
    }

    [Fact]
    public async Task 삭제된_파일은_인덱스에서_제거된다()
    {
        var docs = Path.Combine(_dir.Path, "docs");
        var file = _dir.WriteFile("docs/removed.txt", "삭제될 내용입니다");
        await _index.IndexAsync([docs]);
        Assert.Single(_index.Search("삭제될 내용"));

        File.Delete(file);
        var stats = await _index.IndexAsync([docs]);

        Assert.Equal(1, stats.Removed);
        Assert.Empty(_index.Search("삭제될 내용"));
        Assert.Equal(0, _index.DocumentCount);
    }

    [Fact]
    public async Task 짧은_검색어는_LIKE_폴백으로_동작한다()
    {
        var docs = Path.Combine(_dir.Path, "docs");
        _dir.WriteFile("docs/a.txt", "AB 코드명 문서");

        await _index.IndexAsync([docs]);

        Assert.Single(_index.Search("AB"));
    }
}
