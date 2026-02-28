using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InspectorAPI.Core.Models;

namespace InspectorAPI.Core.ViewModels;

public enum CollectionNodeType { Collection, Folder, Request }

public partial class CollectionTreeNodeViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isExpanded;

    public CollectionNodeType NodeType { get; init; }
    public Collection? Collection { get; init; }
    public CollectionFolder? Folder { get; init; }
    public SavedRequest? SavedRequest { get; init; }
    public Guid? ParentCollectionId { get; init; }
    public Guid? ParentFolderId { get; init; }

    public ObservableCollection<CollectionTreeNodeViewModel> Children { get; } = [];

    // Delegates set by MainViewModel when building the tree
    private Action<CollectionTreeNodeViewModel>? _openAction;
    private Action<CollectionTreeNodeViewModel>? _deleteAction;
    private Action<CollectionTreeNodeViewModel>? _newFolderAction;

    // Computed visibility helpers (used in AXAML without converters)
    public bool IsRequest => NodeType == CollectionNodeType.Request;
    public bool IsNotRequest => NodeType != CollectionNodeType.Request;
    public bool IsCollection => NodeType == CollectionNodeType.Collection;
    public bool IsCollectionOrFolder => NodeType is CollectionNodeType.Collection or CollectionNodeType.Folder;

    public string Icon => NodeType switch
    {
        CollectionNodeType.Collection => "📁",
        CollectionNodeType.Folder => "📂",
        _ => string.Empty
    };

    public string MethodBadge => SavedRequest?.Request.Method ?? string.Empty;

    public void SetActions(
        Action<CollectionTreeNodeViewModel>? openAction,
        Action<CollectionTreeNodeViewModel>? deleteAction,
        Action<CollectionTreeNodeViewModel>? newFolderAction)
    {
        _openAction = openAction;
        _deleteAction = deleteAction;
        _newFolderAction = newFolderAction;
    }

    [RelayCommand]
    private void Open() => _openAction?.Invoke(this);

    [RelayCommand]
    private void Delete() => _deleteAction?.Invoke(this);

    [RelayCommand]
    private void NewFolder() => _newFolderAction?.Invoke(this);
}
