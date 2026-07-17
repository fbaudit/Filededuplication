using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDedup.App.Services;
using FileDedup.Core.Index;

namespace FileDedup.App.ViewModels;

public sealed record ContentHitViewModel(string FileName, string FullPath, string Snippet);

/// <summary>내용 기반 검색 탭 (SQLite FTS5).</summary>
public partial class ContentSearchViewModel : ObservableObject
{
    private readonly ContentIndex _index;
    private CancellationTokenSource? _indexCts;
    private CancellationTokenSource? _searchCts;

    public ContentSearchViewModel(AppSettings settings)
    {
        _index = new ContentIndex(Path.Combine(AppSettings.AppDataDir, "content.db"));
        foreach (var folder in settings.ContentFolders)
            Folders.Add(folder);
        var count = _index.DocumentCount;
        if (count > 0)
            IndexStatus = $"인덱스된 문서 {count:N0}건";
    }

    public Func<Task<string?>>? PickFolderAsync { get; set; }

    public ObservableCollection<string> Folders { get; } = [];

    [ObservableProperty] private string? _selectedFolder;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _indexStatus = "인덱싱할 폴더를 추가하세요.";
    [ObservableProperty] private string _resultStatus = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<ContentHitViewModel> Results { get; } = [];

    [RelayCommand]
    private async Task AddFolder()
    {
        if (PickFolderAsync is null) return;
        var folder = await PickFolderAsync();
        if (folder is not null && !Folders.Contains(folder))
            Folders.Add(folder);
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        if (SelectedFolder is not null)
            Folders.Remove(SelectedFolder);
    }

    [RelayCommand]
    private async Task RunIndexing()
    {
        if (IsBusy || Folders.Count == 0) return;
        IsBusy = true;
        _indexCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ContentIndexProgress>(p =>
                IndexStatus = $"인덱싱 중... {p.Processed:N0}/{p.Total:N0}");
            var stats = await _index.IndexAsync(Folders.ToList(), progress: progress, ct: _indexCts.Token);
            IndexStatus = $"인덱싱 완료 — 신규/갱신 {stats.Indexed:N0}, 변경없음 {stats.Skipped:N0}, " +
                          $"제거 {stats.Removed:N0}, 추출불가 {stats.Failed:N0} " +
                          $"(총 {_index.DocumentCount:N0}건)";
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
            _indexCts = null;
        }
    }

    [RelayCommand]
    private void CancelIndexing() => _indexCts?.Cancel();

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
            await Task.Delay(250, ct);
            if (string.IsNullOrWhiteSpace(query))
            {
                Results.Clear();
                ResultStatus = "";
                return;
            }
            var hits = await Task.Run(() => _index.Search(query, maxResults: 200), ct);
            if (ct.IsCancellationRequested) return;

            Results.Clear();
            foreach (var hit in hits)
                Results.Add(new ContentHitViewModel(
                    Path.GetFileName(hit.Path), hit.Path, hit.Snippet.Trim()));
            ResultStatus = $"{hits.Count:N0}개 문서에서 발견";
        }
        catch (OperationCanceledException) { /* 새 입력으로 대체됨 */ }
        catch (Exception ex)
        {
            ResultStatus = $"검색 오류: {ex.Message}";
        }
    }

    public void OpenHit(ContentHitViewModel? hit)
    {
        if (hit is not null) Shell.RevealInExplorer(hit.FullPath);
    }
}
