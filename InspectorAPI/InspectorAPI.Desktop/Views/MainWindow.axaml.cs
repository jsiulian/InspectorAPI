using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using InspectorAPI.Core.ViewModels;

namespace InspectorAPI.Desktop.Views;

public partial class MainWindow : Window
{
    private CollectionTreeNodeViewModel? _draggedItem;
    private CollectionTreeNodeViewModel? _targetItem;
    private bool _isDragging = false;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;

        Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;

            vm.PickSaveFilePath = async suggestedName =>
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Collection",
                    SuggestedFileName = suggestedName,
                    FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
                });
                return file?.TryGetLocalPath();
            };

            // Auto-focus the main textbox when a dialog opens
            var nameBox = this.FindControl<TextBox>("NameDialogTextBox");
            var saveBox = this.FindControl<TextBox>("SaveNameTextBox");

            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsNameDialogOpen) && vm.IsNameDialogOpen)
                    Dispatcher.UIThread.Post(() => { nameBox?.Focus(); nameBox?.SelectAll(); }, DispatcherPriority.Loaded);
                else if (e.PropertyName == nameof(MainViewModel.IsSaveDialogOpen) && vm.IsSaveDialogOpen)
                    Dispatcher.UIThread.Post(() => { saveBox?.Focus(); saveBox?.SelectAll(); }, DispatcherPriority.Loaded);
            };
        };
    }

    private async void OnImportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Collection",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });

        if (files.Count == 0) return;

        var localPath = files[0].TryGetLocalPath();
        if (localPath is null) return;

        var json = await File.ReadAllTextAsync(localPath);
        await vm.ImportCollectionCommand.ExecuteAsync(json);
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

    // Drag and Drop for TreeView and TreeViewItem
    private void OnCollectionTreeDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Data.Contains("CollectionTreeNodeViewModel"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnCollectionTreeDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Data.Contains("CollectionTreeNodeViewModel") && _draggedItem != null)
        {
            if (_targetItem == null && sender is TreeView)
            {
                // Move to root level
                _draggedItem.Parent?.Children.Remove(_draggedItem);
                vm.CollectionTree.Add(_draggedItem);
                _draggedItem.Parent = null;
            }
            else if (_targetItem != null && _targetItem.IsCollectionOrFolder)
            {
                // Move into target folder
                _draggedItem.Parent?.Children.Remove(_draggedItem);
                _targetItem.Children.Add(_draggedItem);
                _draggedItem.Parent = _targetItem;
            }
            else if (_targetItem != null && _targetItem.Parent != null)
            {
                // Move after target item in same parent
                var parent = _targetItem.Parent;
                int targetIndex = parent.Children.IndexOf(_targetItem);
                parent.Children.Remove(_draggedItem);
                parent.Children.Insert(targetIndex + 1, _draggedItem);
                _draggedItem.Parent = parent;
            }

            _draggedItem = null;
            _targetItem = null;
            _isDragging = false;
            e.Handled = true;
        }
    }

    private void OnTreeNodeDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border border && border.DataContext is CollectionTreeNodeViewModel targetNode)
        {
            _targetItem = targetNode;
            if (targetNode.IsCollectionOrFolder)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }
    }

    private void OnTreeNodeDragLeave(object? sender, DragEventArgs e)
    {
        _targetItem = null;
        e.Handled = true;
    }

    private void OnTreeNodeDragOver(object? sender, DragEventArgs e)
    {
        if (sender is Border border && border.DataContext is CollectionTreeNodeViewModel targetNode && targetNode.IsCollectionOrFolder)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnTreeNodeDrop(object? sender, DragEventArgs e)
    {
        if (sender is Border border && border.DataContext is CollectionTreeNodeViewModel targetNode && e.Data.Contains("CollectionTreeNodeViewModel"))
        {
            _draggedItem = (CollectionTreeNodeViewModel)e.Data.Get("CollectionTreeNodeViewModel");

            if (_draggedItem != null && _draggedItem != targetNode)
            {
                if (targetNode.IsCollectionOrFolder)
                {
                    // Move into target folder
                    _draggedItem.Parent?.Children.Remove(_draggedItem);
                    targetNode.Children.Add(_draggedItem);
                    _draggedItem.Parent = targetNode;
                }
                else if (targetNode.Parent != null)
                {
                    // Move after target item
                    var parent = targetNode.Parent;
                    int targetIndex = parent.Children.IndexOf(targetNode);
                    parent.Children.Remove(_draggedItem);
                    parent.Children.Insert(targetIndex + 1, _draggedItem);
                    _draggedItem.Parent = parent;
                }
            }

            e.Handled = true;
        }
    }
}