using Avalonia.Controls;
using Avalonia.Input;
using InspectorAPI.Core.ViewModels;

namespace InspectorAPI.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTreeNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: CollectionTreeNodeViewModel { IsRequest: true } node })
            node.OpenCommand.Execute(null);
    }
}
