using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using InspectorAPI.Core.Services;
using InspectorAPI.Core.ViewModels;
using InspectorAPI.Desktop.Views;

namespace InspectorAPI.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var collectionService = new CollectionService();
            var httpRequestService = new HttpRequestService();
            var mainViewModel = new MainViewModel(collectionService, httpRequestService);

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.MainWindow = mainWindow;

            // Initialize async (load collections, open default tab)
            mainWindow.Opened += async (_, _) => await mainViewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
