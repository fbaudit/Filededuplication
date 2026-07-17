using FileDedup.Core.Dedup;

namespace FileDedup.Core.Delete;

public enum DisposeMethod
{
    /// <summary>휴지통으로 이동 (기본, 복구 가능).</summary>
    RecycleBin,
    /// <summary>영구 삭제.</summary>
    Permanent,
    /// <summary>지정 폴더로 이동.</summary>
    MoveTo,
}

public sealed record DisposeOutcome(FileRecord File, bool Success, string Action, string? Error);

/// <summary>삭제/이동 실행기. 실패한 파일은 건너뛰고 결과에 기록한다.</summary>
public sealed class FileDisposer(IRecycleBin recycleBin)
{
    public List<DisposeOutcome> Dispose(
        IEnumerable<FileRecord> files,
        DisposeMethod method,
        string? moveTargetFolder = null,
        CancellationToken ct = default)
    {
        if (method == DisposeMethod.MoveTo && string.IsNullOrWhiteSpace(moveTargetFolder))
            throw new ArgumentException("이동 대상 폴더가 필요합니다.", nameof(moveTargetFolder));
        if (method == DisposeMethod.RecycleBin && !recycleBin.IsSupported)
            throw new PlatformNotSupportedException("휴지통 이동은 Windows에서만 지원됩니다.");

        var results = new List<DisposeOutcome>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (method)
                {
                    case DisposeMethod.RecycleBin:
                        recycleBin.Send(file.FullPath);
                        results.Add(new DisposeOutcome(file, true, "휴지통 이동", null));
                        break;
                    case DisposeMethod.Permanent:
                        File.Delete(file.FullPath);
                        results.Add(new DisposeOutcome(file, true, "영구 삭제", null));
                        break;
                    case DisposeMethod.MoveTo:
                        var target = MoveWithoutOverwrite(file.FullPath, moveTargetFolder!);
                        results.Add(new DisposeOutcome(file, true, $"이동: {target}", null));
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new DisposeOutcome(file, false, method.ToString(), ex.Message));
            }
        }
        return results;
    }

    /// <summary>대상 폴더에 같은 이름이 있으면 "이름 (1).확장자" 식으로 회피하며 이동.</summary>
    private static string MoveWithoutOverwrite(string sourcePath, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var candidate = Path.Combine(targetFolder, name + ext);
        for (var i = 1; File.Exists(candidate); i++)
            candidate = Path.Combine(targetFolder, $"{name} ({i}){ext}");
        File.Move(sourcePath, candidate);
        return candidate;
    }
}
