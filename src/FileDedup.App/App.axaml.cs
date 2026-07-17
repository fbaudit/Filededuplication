using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileDedup.App.ViewModels;
using FileDedup.App.Views;

namespace FileDedup.App;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.Exit += (_, _) => mainViewModel.SaveSettings();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
