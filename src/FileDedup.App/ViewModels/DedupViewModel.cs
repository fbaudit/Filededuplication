using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDedup.App.Services;
using FileDedup.Core.Dedup;
using FileDedup.Core.Delete;
using FileDedup.Core.Scanning;

namespace FileDedup.App.ViewModels;

public partial class FileItemViewModel(FileRecord record) : ObservableObject
{
    public FileRecord Record { get; } = record;

    [ObservableProperty]
    private bool _isChecked;

    public string FullPath => Record.FullPath;
    public string SizeText => Format.Bytes(Record.Length);
    public string ModifiedText => Format.LocalTime(Record.Entry.ModifiedUtc);
    public string HashText => Record.Hashes.Sha256 ?? Record.Hashes.Md5 ?? Record.Hashes.Crc32 ?? "";
}

public sealed class DuplicateGroupViewModel
{
    public required string Header { get; init; }
    public required ObservableCollection<FileItemViewModel> Files { get; init; }
}

public partial class DedupViewModel : ObservableObject
{
    private readonly IRecycleBin _recycleBin;
    private CancellationTokenSource? _cts;
    private List<DuplicateGroup> _lastGroups = [];

    public DedupViewModel(IRecycleBin recycleBin, AppSettings settings)
    {
        _recycleBin = recycleBin;
        foreach (var folder in settings.DedupFolders)
            Folders.Add(folder);
        ExcludePatterns = settings.ExcludePatterns;
    }

    // ── 뷰가 주입하는 대화상자 델리게이트 ──
    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public ObservableCollection<string> Folders { get; } = [];

    [ObservableProperty] private string? _selectedFolder;

    // 중복 판별 기준
    [ObservableProperty] private bool _byFileName;
    [ObservableProperty] private bool _bySize = true;
    [ObservableProperty] private bool _byCreated;
    [ObservableProperty] private bool _byModified;
    [ObservableProperty] private bool _byCrc32;
    [ObservableProperty] private bool _byMd5;
    [ObservableProperty] private bool _bySha256 = true;
    [ObservableProperty] private bool _ignoreCase = true;
    [ObservableProperty] private string _excludePatterns = "";
    [ObservableProperty] private string _minSizeKb = "0";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "폴더를 추가하고 검사를 시작하세요.";
    [ObservableProperty] private string _summaryText = "";

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    // 자동 선택 규칙 / 처리 방식 (ComboBox 인덱스)
    [ObservableProperty] private int _keepPolicyIndex;
    [ObservableProperty] private string? _preferredFolder;
    [ObservableProperty] private int _disposeMethodIndex;
    [ObservableProperty] private string? _moveTargetFolder;
    [ObservableProperty] private bool _writeReport = true;

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
    private async Task PickPreferredFolder()
    {
        if (PickFolderAsync is null) return;
        PreferredFolder = await PickFolderAsync() ?? PreferredFolder;
    }

    [RelayCommand]
    private async Task PickMoveTarget()
    {
        if (PickFolderAsync is null) return;
        MoveTargetFolder = await PickFolderAsync() ?? MoveTargetFolder;
    }

    private DuplicateCriteria BuildCriteria()
    {
        var criteria = DuplicateCriteria.None;
        if (ByFileName) criteria |= DuplicateCriteria.FileName;
        if (BySize) criteria |= DuplicateCriteria.Size;
        if (ByCreated) criteria |= DuplicateCriteria.CreatedDate;
        if (ByModified) criteria |= DuplicateCriteria.ModifiedDate;
        if (ByCrc32) criteria |= DuplicateCriteria.Crc32;
        if (ByMd5) criteria |= DuplicateCriteria.Md5;
        if (BySha256) criteria |= DuplicateCriteria.Sha256;
        return criteria;
    }

    [RelayCommand]
    private async Task Scan()
    {
        if (IsBusy) return;
        if (Folders.Count == 0)
        {
            StatusText = "검사할 폴더를 먼저 추가하세요.";
            return;
        }
        var criteria = BuildCriteria();
        if (criteria == DuplicateCriteria.None)
        {
            StatusText = "중복 판별 기준을 하나 이상 선택하세요.";
            return;
        }

        IsBusy = true;
        Groups.Clear();
        SummaryText = "";
        _cts = new CancellationTokenSource();
        try
        {
            long.TryParse(MinSizeKb, out var minKb);
            var scanOptions = new ScanOptions
            {
                ExcludePatterns = ExcludePatterns
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                MinFileSize = Math.Max(0, minKb) * 1024,
            };

            var scanProgress = new Progress<ScanProgress>(p =>
                StatusText = $"스캔 중... {p.FilesFound:N0}개 파일 발견");
            var files = await FolderScanner.ScanAsync(Folders, scanOptions, scanProgress, _cts.Token);

            var dedupProgress = new Progress<DedupProgress>(p => StatusText = p.Stage switch
            {
                DedupStage.PartialHashing => $"부분 해시 계산 중... {p.ProcessedFiles:N0}/{p.TotalFiles:N0}",
                DedupStage.FullHashing =>
                    $"전체 해시 계산 중... {p.ProcessedFiles:N0}/{p.TotalFiles:N0} ({Format.Bytes(p.ProcessedBytes)})",
                _ => StatusText,
            });
            var result = await DuplicateFinder.FindAsync(files,
                new DedupOptions { Criteria = criteria, IgnoreCase = IgnoreCase },
                dedupProgress, _cts.Token);

            _lastGroups = result.Groups;
            for (var i = 0; i < result.Groups.Count; i++)
            {
                var group = result.Groups[i];
                Groups.Add(new DuplicateGroupViewModel
                {
                    Header = $"그룹 {i + 1} — {group.Files.Count}개 파일, " +
                             $"{Format.Bytes(group.WastedBytes)} 절약 가능",
                    Files = new ObservableCollection<FileItemViewModel>(
                        group.Files.Select(f => new FileItemViewModel(f))),
                });
            }

            var wasted = result.Groups.Sum(g => g.WastedBytes);
            SummaryText = $"중복 그룹 {result.Groups.Count:N0}개 / " +
                          $"중복 파일 {result.Groups.Sum(g => g.Files.Count - 1):N0}개 / " +
                          $"절약 가능 {Format.Bytes(wasted)}";
            StatusText = result.Errors.Count > 0
                ? $"검사 완료 (읽기 실패 {result.Errors.Count}건 제외)"
                : "검사 완료";
        }
        catch (OperationCanceledException)
        {
            StatusText = "검사가 취소되었습니다.";
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>자동 선택 규칙 적용 — 그룹당 1개는 반드시 보존된다.</summary>
    [RelayCommand]
    private void ApplyRule()
    {
        if (_lastGroups.Count == 0) return;
        var policy = KeepPolicyIndex switch
        {
            0 => KeepPolicy.Newest,
            1 => KeepPolicy.Oldest,
            2 => KeepPolicy.ShortestPath,
            _ => KeepPolicy.PreferFolder,
        };
        var toRemove = SelectionRules.Apply(_lastGroups, policy, PreferredFolder);
        foreach (var group in Groups)
            foreach (var file in group.Files)
                file.IsChecked = toRemove.Contains(file.Record);
        StatusText = $"삭제 대상 {toRemove.Count:N0}개 선택됨";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var group in Groups)
            foreach (var file in group.Files)
                file.IsChecked = false;
    }

    [RelayCommand]
    private async Task Execute()
    {
        if (IsBusy) return;
        var checkedItems = Groups.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList();
        if (checkedItems.Count == 0)
        {
            StatusText = "삭제 대상으로 선택된 파일이 없습니다.";
            return;
        }
        // 그룹 전체 삭제 방지
        foreach (var group in Groups)
        {
            if (group.Files.Count > 0 && group.Files.All(f => f.IsChecked))
            {
                StatusText = $"그룹 전체를 삭제할 수 없습니다: {group.Header}";
                return;
            }
        }

        var method = DisposeMethodIndex switch
        {
            0 => DisposeMethod.RecycleBin,
            1 => DisposeMethod.MoveTo,
            _ => DisposeMethod.Permanent,
        };
        if (method == DisposeMethod.RecycleBin && !_recycleBin.IsSupported)
        {
            StatusText = "이 환경에서는 휴지통을 지원하지 않습니다. 이동 또는 영구 삭제를 선택하세요.";
            return;
        }
        if (method == DisposeMethod.MoveTo && string.IsNullOrWhiteSpace(MoveTargetFolder))
        {
            StatusText = "이동 대상 폴더를 선택하세요.";
            return;
        }

        var actionName = method switch
        {
            DisposeMethod.RecycleBin => "휴지통으로 이동",
            DisposeMethod.MoveTo => $"'{MoveTargetFolder}'(으)로 이동",
            _ => "영구 삭제",
        };
        var totalBytes = checkedItems.Sum(f => f.Record.Length);
        if (ConfirmAsync is not null)
        {
            var confirmed = await ConfirmAsync("처리 확인",
                $"선택된 {checkedItems.Count:N0}개 파일({Format.Bytes(totalBytes)})을 {actionName}합니다.\n계속할까요?");
            if (!confirmed) return;
        }

        IsBusy = true;
        try
        {
            var disposer = new FileDisposer(_recycleBin);
            var outcomes = await Task.Run(() => disposer.Dispose(
                checkedItems.Select(f => f.Record), method, MoveTargetFolder));

            if (WriteReport)
            {
                var reportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"FileDedup_보고서_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                CsvReportWriter.Write(reportPath, _lastGroups,
                    outcomes.ToDictionary(o => o.File));
                StatusText = $"보고서 저장: {reportPath}";
            }

            var succeeded = outcomes.Where(o => o.Success)
                .Select(o => o.File).ToHashSet();
            foreach (var group in Groups.ToList())
            {
                foreach (var file in group.Files.Where(f => succeeded.Contains(f.Record)).ToList())
                    group.Files.Remove(file);
                if (group.Files.Count <= 1)
                    Groups.Remove(group);
            }

            var failed = outcomes.Count(o => !o.Success);
            SummaryText = failed > 0
                ? $"{succeeded.Count:N0}개 처리 완료, {failed:N0}개 실패"
                : $"{succeeded.Count:N0}개 처리 완료 ({Format.Bytes(totalBytes)} 확보)";
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenFile(FileItemViewModel? item)
    {
        if (item is not null) Shell.RevealInExplorer(item.FullPath);
    }
}
