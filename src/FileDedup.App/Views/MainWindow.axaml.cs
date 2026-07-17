using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FileDedup.App.ViewModels;

namespace FileDedup.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.Dedup.PickFolderAsync = PickSingleFolderAsync;
            vm.Dedup.ConfirmAsync = ConfirmAsync;
            vm.Search.PickFoldersAsync = PickMultipleFoldersAsync;
            vm.Content.PickFolderAsync = PickSingleFolderAsync;
        };
    }

    private async Task<string?> PickSingleFolderAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "폴더 선택", AllowMultiple = false });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    private async Task<IReadOnlyList<string>> PickMultipleFoldersAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "인덱싱할 폴더 선택", AllowMultiple = true });
        return result
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var okButton = new Button
        {
            Content = "실행", MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var cancelButton = new Button
        {
            Content = "취소", MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { okButton, cancelButton },
                    },
                },
            },
        };
        okButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        cancelButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private void OnDedupFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm
            && sender is Control { DataContext: FileItemViewModel item })
            vm.Dedup.OpenFile(item);
    }

    private void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm
            && SearchResultsGrid.SelectedItem is SearchHitViewModel hit)
            vm.Search.OpenHit(hit);
    }

    private void OnContentResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm
            && ContentResultsList.SelectedItem is ContentHitViewModel hit)
            vm.Content.OpenHit(hit);
    }
}
