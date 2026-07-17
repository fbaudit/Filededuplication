using FileDedup.Core.Dedup;
using FileDedup.Core.Hashing;
using FileDedup.Core.Scanning;

namespace FileDedup.Core.Tests;

public class DuplicateFinderTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private Task<DedupResult> RunAsync(DuplicateCriteria criteria)
    {
        var files = FolderScanner.Scan([_dir.Path], new ScanOptions());
        return DuplicateFinder.FindAsync(files, new DedupOptions { Criteria = criteria });
    }

    [Fact]
    public async Task Sha256_기준_동일_내용_파일을_그룹으로_묶는다()
    {
        _dir.WriteFile("a.bin", "동일한 내용입니다");
        _dir.WriteFile("sub/b.bin", "동일한 내용입니다");
        _dir.WriteFile("c.bin", "다른 내용");

        var result = await RunAsync(DuplicateCriteria.Sha256);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.All(group.Files, f => Assert.NotNull(f.Hashes.Sha256));
        Assert.Equal(group.Files[0].Hashes.Sha256, group.Files[1].Hashes.Sha256);
    }

    [Fact]
    public async Task 크기만_같고_내용이_다르면_해시_기준에서_중복이_아니다()
    {
        _dir.WriteFile("a.bin", new byte[] { 1, 2, 3, 4 });
        _dir.WriteFile("b.bin", new byte[] { 9, 9, 9, 9 });

        var result = await RunAsync(DuplicateCriteria.Md5);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task 부분해시_경계보다_큰_파일도_정확히_판별한다()
    {
        // 선두 64KB는 같고 뒷부분만 다른 파일: 부분해시는 같아도 전체 해시로 구분해야 함
        var common = new byte[FileHasher.PartialHashLength + 100];
        Random.Shared.NextBytes(common);
        var copy = (byte[])common.Clone();
        var different = (byte[])common.Clone();
        different[^1] ^= 0xFF;

        _dir.WriteFile("orig.bin", common);
        _dir.WriteFile("copy.bin", copy);
        _dir.WriteFile("tail-diff.bin", different);

        var result = await RunAsync(DuplicateCriteria.Sha256);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.DoesNotContain(group.Files, f => f.Entry.Name == "tail-diff.bin");
    }

    [Fact]
    public async Task 파일명과_크기_조합_기준은_둘_다_같아야_중복이다()
    {
        _dir.WriteFile("same.txt", "abcd");        // 이름 같음, 크기 같음(4)
        _dir.WriteFile("sub/same.txt", "wxyz");    // 이름 같음, 크기 같음(4) → 중복
        _dir.WriteFile("sub2/same.txt", "긴내용이라크기다름");
        _dir.WriteFile("other.txt", "abcd");       // 크기 같지만 이름 다름

        var result = await RunAsync(DuplicateCriteria.FileName | DuplicateCriteria.Size);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.All(group.Files, f => Assert.Equal("same.txt", f.Entry.Name));
    }

    [Fact]
    public async Task 세_가지_해시를_모두_선택하면_모든_해시값이_계산된다()
    {
        _dir.WriteFile("a.bin", "중복");
        _dir.WriteFile("b.bin", "중복");

        var result = await RunAsync(
            DuplicateCriteria.Crc32 | DuplicateCriteria.Md5 | DuplicateCriteria.Sha256);

        var group = Assert.Single(result.Groups);
        Assert.All(group.Files, f =>
        {
            Assert.NotNull(f.Hashes.Crc32);
            Assert.NotNull(f.Hashes.Md5);
            Assert.NotNull(f.Hashes.Sha256);
        });
    }

    [Fact]
    public async Task 기준이_없으면_예외를_던진다()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(DuplicateCriteria.None));
    }

    [Fact]
    public async Task 그룹은_절약_가능_용량_내림차순으로_정렬된다()
    {
        _dir.WriteFile("big1.bin", new byte[10_000]);
        _dir.WriteFile("big2.bin", new byte[10_000]);
        _dir.WriteFile("small1.bin", new byte[10]);
        _dir.WriteFile("small2.bin", new byte[10]);

        var result = await RunAsync(DuplicateCriteria.Sha256);

        Assert.Equal(2, result.Groups.Count);
        Assert.True(result.Groups[0].WastedBytes >= result.Groups[1].WastedBytes);
        Assert.Equal(10_000, result.Groups[0].WastedBytes);
    }
}
