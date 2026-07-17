using System.Diagnostics;

namespace FileDedup.App.Services;

public static class Shell
{
    /// <summary>탐색기에서 파일 위치를 열고 해당 파일을 선택한다.</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", ["-R", path]);
            else
                Process.Start("xdg-open", Path.GetDirectoryName(path) ?? path);
        }
        catch { /* 탐색기 실행 실패는 무시 */ }
    }
}
