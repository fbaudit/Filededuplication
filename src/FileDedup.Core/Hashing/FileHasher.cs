using System.IO.Hashing;
using System.Security.Cryptography;

namespace FileDedup.Core.Hashing;

[Flags]
public enum HashAlgorithms
{
    None = 0,
    Crc32 = 1,
    Md5 = 2,
    Sha256 = 4,
}

public sealed record FileHashes(string? Crc32, string? Md5, string? Sha256);

/// <summary>파일을 한 번만 읽으면서 선택된 해시들을 동시에 계산한다.</summary>
public static class FileHasher
{
    public const int PartialHashLength = 64 * 1024;
    private const int BufferSize = 1024 * 1024;

    /// <summary>선두 <see cref="PartialHashLength"/> 바이트의 XxHash64 — 전체 해시 전 후보 축소용.</summary>
    public static async Task<ulong> ComputePartialAsync(string path, CancellationToken ct = default)
    {
        var buffer = new byte[PartialHashLength];
        await using var stream = OpenRead(path);
        int total = 0, read;
        while (total < buffer.Length &&
               (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
            total += read;
        return XxHash64.HashToUInt64(buffer.AsSpan(0, total));
    }

    public static async Task<FileHashes> ComputeAsync(
        string path, HashAlgorithms algorithms, CancellationToken ct = default)
    {
        var crc = algorithms.HasFlag(HashAlgorithms.Crc32) ? new Crc32() : null;
        using var md5 = algorithms.HasFlag(HashAlgorithms.Md5) ? MD5.Create() : null;
        using var sha = algorithms.HasFlag(HashAlgorithms.Sha256) ? SHA256.Create() : null;

        var buffer = new byte[BufferSize];
        await using (var stream = OpenRead(path))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                crc?.Append(buffer.AsSpan(0, read));
                md5?.TransformBlock(buffer, 0, read, null, 0);
                sha?.TransformBlock(buffer, 0, read, null, 0);
            }
        }

        md5?.TransformFinalBlock([], 0, 0);
        sha?.TransformFinalBlock([], 0, 0);

        return new FileHashes(
            crc is null ? null : Convert.ToHexString(crc.GetCurrentHash().Reverse().ToArray()),
            md5?.Hash is null ? null : Convert.ToHexString(md5.Hash),
            sha?.Hash is null ? null : Convert.ToHexString(sha.Hash));
    }

    private static FileStream OpenRead(string path) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
        BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
}
