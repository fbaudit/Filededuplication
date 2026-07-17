using CommunityToolkit.Mvvm.ComponentModel;
using FileDedup.App.Services;
using FileDedup.Core.Delete;
using FileDedup.Core.Search;

namespace FileDedup.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public DedupViewModel Dedup { get; }
    public SearchViewModel Search { get; }
    public ContentSearchViewModel Content { get; }

    public MainViewModel()
    {
        _settings = AppSettings.Load();

        IRecycleBin recycleBin = OperatingSystem.IsWindows()
            ? new FileDedup.Windows.WindowsRecycleBin()
            : new UnsupportedRecycleBin();
        IVolumeEnumerator volumeEnumerator = OperatingSystem.IsWindows()
            ? new FileDedup.Windows.MftEnumerator()
            : new NullVolumeEnumerator();

        Dedup = new DedupViewModel(recycleBin, _settings);
        Search = new SearchViewModel(volumeEnumerator);
        Content = new ContentSearchViewModel(_settings);
    }

    public void SaveSettings()
    {
        _settings.DedupFolders = Dedup.Folders.ToList();
        _settings.ContentFolders = Content.Folders.ToList();
        _settings.ExcludePatterns = Dedup.ExcludePatterns;
        _settings.Save();
    }

    private sealed class NullVolumeEnumerator : IVolumeEnumerator
    {
        public bool IsSupported(string driveRoot) => false;
        public List<VolumeItem> Enumerate(string driveRoot, CancellationToken ct = default)
            => throw new PlatformNotSupportedException("MFT 열거는 Windows에서만 지원됩니다.");
    }
}
