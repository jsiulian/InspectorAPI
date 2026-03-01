using Avalonia.Controls;
using Avalonia.Input;
using InspectorAPI.Core.ViewModels;

namespace InspectorAPI.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }

    // Enter/Esc shortcuts for all dialogs — handled at window level so focus
    // placement inside the dialog doesn't matter.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.IsNameDialogOpen)
        {
            if (e.Key == Key.Enter)  { vm.ConfirmNameDialogCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.CancelNameDialogCommand.Execute(null); e.Handled = true; }
        }
        else if (vm.IsSaveDialogOpen)
        {
            if (e.Key == Key.Enter)  { vm.SaveRequestCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.CancelSaveDialogCommand.Execute(null); e.Handled = true; }
        }
        else if (vm.IsDeleteDialogOpen)
        {
            if (e.Key == Key.Enter)  { vm.ConfirmDeleteCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.CancelDeleteCommand.Execute(null); e.Handled = true; }
        }
    }

    private void OnTreeNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: CollectionTreeNodeViewModel { IsRequest: true } node })
            node.OpenCommand.Execute(null);
    }

    // Enter → open request in tab; Delete → trigger delete confirmation.
    private void OnCollectionTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TreeView { SelectedItem: CollectionTreeNodeViewModel node }) return;
        if (e.Key == Key.Return && node.IsRequest) { node.OpenCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Delete) { node.DeleteCommand.Execute(null); e.Handled = true; }
    }
}
