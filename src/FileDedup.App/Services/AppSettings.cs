using System.Text.Json;

namespace FileDedup.App.Services;

/// <summary>간단한 사용자 설정 (LocalAppData\FileDedup\settings.json).</summary>
public sealed class AppSettings
{
    public List<string> DedupFolders { get; set; } = [];
    public List<string> ContentFolders { get; set; } = [];
    public string ExcludePatterns { get; set; } = "*.tmp;~$*";

    public static string AppDataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileDedup");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                       ?? new AppSettings();
        }
        catch { /* 손상된 설정은 기본값으로 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 설정 저장 실패는 치명적이지 않음 */ }
    }
}
