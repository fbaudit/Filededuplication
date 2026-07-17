using FileDedup.Core.Hashing;

namespace FileDedup.Core.Tests;

public class FileHasherTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task 알려진_입력의_해시값이_표준과_일치한다()
    {
        // "abc"의 표준 해시값 (RFC 및 CRC-32/ISO-HDLC 검증 벡터)
        var path = _dir.WriteFile("abc.txt", "abc");
        var hashes = await FileHasher.ComputeAsync(path,
            HashAlgorithms.Crc32 | HashAlgorithms.Md5 | HashAlgorithms.Sha256);

        Assert.Equal("352441C2", hashes.Crc32);
        Assert.Equal("900150983CD24FB0D6963F7D28E17F72", hashes.Md5);
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            hashes.Sha256);
    }

    [Fact]
    public async Task 선택하지_않은_알고리즘은_계산하지_않는다()
    {
        var path = _dir.WriteFile("f.txt", "data");
        var hashes = await FileHasher.ComputeAsync(path, HashAlgorithms.Sha256);

        Assert.Null(hashes.Crc32);
        Assert.Null(hashes.Md5);
        Assert.NotNull(hashes.Sha256);
    }

    [Fact]
    public async Task 부분해시는_선두_64KB만_반영한다()
    {
        var data = new byte[FileHasher.PartialHashLength + 10];
        Random.Shared.NextBytes(data);
        var tailChanged = (byte[])data.Clone();
        tailChanged[^1] ^= 0xFF;
        var headChanged = (byte[])data.Clone();
        headChanged[0] ^= 0xFF;

        var p1 = await FileHasher.ComputePartialAsync(_dir.WriteFile("a.bin", data));
        var p2 = await FileHasher.ComputePartialAsync(_dir.WriteFile("b.bin", tailChanged));
        var p3 = await FileHasher.ComputePartialAsync(_dir.WriteFile("c.bin", headChanged));

        Assert.Equal(p1, p2);    // 뒷부분 차이는 부분해시에 안 보임
        Assert.NotEqual(p1, p3); // 앞부분 차이는 보임
    }
}
