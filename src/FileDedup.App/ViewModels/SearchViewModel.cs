using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDedup.App.Services;
using FileDedup.Core.Scanning;
using FileDedup.Core.Search;

namespace FileDedup.App.ViewModels;

public sealed record SearchHitViewModel(string Name, string FullPath, string SizeText, string ModifiedText)
{
    public static SearchHitViewModel From(SearchHit hit) => new(
        hit.Name, hit.FullPath, Format.Bytes(hit.Size), Format.LocalTime(hit.ModifiedUtc));
}

/// <summary>파일명 즉시 검색 탭 (Everything 벤치마킹).</summary>
public partial class SearchViewModel(IVolumeEnumerator volumeEnumerator) : ObservableObject
{
    private readonly List<FileNameIndex> _indexes = [];
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _buildCts;

    public Func<Task<IReadOnlyList<string>>>? PickFoldersAsync { get; set; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _indexStatus = "인덱스가 아직 없습니다. 아래 버튼으로 인덱스를 만드세요.";
    [ObservableProperty] private string _resultStatus = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<SearchHitViewModel> Results { get; } = [];

    public bool IsMftAvailable => OperatingSystem.IsWindows()
        && DriveInfo.GetDrives().Any(d => d is { DriveType: DriveType.Fixed, IsReady: true }
                                          && volumeEnumerator.IsSupported(d.RootDirectory.FullName));

    /// <summary>NTFS MFT로 모든 고정 드라이브 전체 인덱싱 (관리자 권한 필요).</summary>
    [RelayCommand]
    private async Task BuildMftIndex()
    {
        if (IsBusy) return;
        IsBusy = true;
        _buildCts = new CancellationTokenSource();
        try
        {
            _indexes.Clear();
            var total = 0;
            foreach (var drive in DriveInfo.GetDrives()
                         .Where(d => d is { DriveType: DriveType.Fixed, IsReady: true }))
            {
                var root = drive.RootDirectory.FullName;
                if (!volumeEnumerator.IsSupported(root)) continue;
                IndexStatus = $"{root} MFT 읽는 중...";
                var index = await Task.Run(() =>
                {
                    var items = volumeEnumerator.Enumerate(root, _buildCts.Token);
                    return FileNameIndex.FromVolumeItems(root, items);
                }, _buildCts.Token);
                _indexes.Add(index);
                total += index.Count;
                IndexStatus = $"인덱싱 완료: {total:N0}개 파일 (MFT 모드)";
            }
            if (_indexes.Count == 0)
                IndexStatus = "MFT를 읽을 수 있는 NTFS 드라이브가 없습니다. 관리자 권한으로 실행했는지 확인하세요.";
        }
        catch (OperationCanceledException)
        {
            IndexStatus = "인덱싱이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            IndexStatus = $"MFT 인덱싱 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _buildCts = null;
        }
    }

    /// <summary>폴더 선택 인덱싱 (비관리자 폴백 모드).</summary>
    [RelayCommand]
    private async Task BuildFolderIndex()
    {
        if (IsBusy || PickFoldersAsync is null) return;
        var folders = await PickFoldersAsync();
        if (folders.Count == 0) return;

        IsBusy = true;
        _buildCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ScanProgress>(p =>
                IndexStatus = $"스캔 중... {p.FilesFound:N0}개 파일");
            var entries = await FolderScanner.ScanAsync(folders,
                new ScanOptions { SkipHiddenAndSystem = false }, progress, _buildCts.Token);
            _indexes.Clear();
            _indexes.Add(FileNameIndex.FromEntries(entries));
            IndexStatus = $"인덱싱 완료: {entries.Count:N0}개 파일 (폴더 모드)";
        }
        catch (OperationCanceledException)
        {
            IndexStatus = "인덱싱이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            IndexStatus = $"인덱싱 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _buildCts = null;
        }
    }

    [RelayCommand]
    private void CancelBuild() => _buildCts?.Cancel();

    /// <summary>타이핑 즉시 검색 (150ms 디바운스).</summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        _ = RunSearchAsync(value, cts.Token);
    }

    private async Task RunSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(150, ct);
            if (string.IsNullOrWhiteSpace(query) || _indexes.Count == 0)
            {
                Results.Clear();
                ResultStatus = "";
                return;
            }
            var hits = await Task.Run(() => _indexes
                .SelectMany(index => index.Search(query, maxResults: 1000, ct))
                .Take(1000)
                .ToList(), ct);
            if (ct.IsCancellationRequested) return;

            Results.Clear();
            foreach (var hit in hits)
                Results.Add(SearchHitViewModel.From(hit));
            ResultStatus = hits.Count >= 1000
                ? $"상위 {hits.Count:N0}개 표시 (더 구체적으로 검색하세요)"
                : $"{hits.Count:N0}개 결과";
        }
        catch (OperationCanceledException) { /* 새 입력으로 대체됨 */ }
    }

    public void OpenHit(SearchHitViewModel? hit)
    {
        if (hit is not null) Shell.RevealInExplorer(hit.FullPath);
    }
}
