using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InspectorAPI.Core.Models;

namespace InspectorAPI.Core.ViewModels;

public enum CollectionNodeType { Collection, Folder, Request }

public partial class CollectionTreeNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlatTargetDisplayName))]
    private string _name = string.Empty;
    [ObservableProperty] private bool _isExpanded;

    public int FlatDepth { get; set; }
    public string FlatTargetDisplayName =>
        new string(' ', FlatDepth * 4) + (FlatDepth == 0 ? "📁 " : "📂 ") + Name;

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
    private Action<CollectionTreeNodeViewModel>? _renameAction;
    private Action<CollectionTreeNodeViewModel>? _newRequestAction;
    private Action<CollectionTreeNodeViewModel>? _exportNativeAction;
    private Action<CollectionTreeNodeViewModel>? _exportPostmanAction;

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
        Action<CollectionTreeNodeViewModel>? newFolderAction,
        Action<CollectionTreeNodeViewModel>? renameAction,
        Action<CollectionTreeNodeViewModel>? newRequestAction = null,
        Action<CollectionTreeNodeViewModel>? exportNativeAction = null,
        Action<CollectionTreeNodeViewModel>? exportPostmanAction = null)
    {
        _openAction = openAction;
        _deleteAction = deleteAction;
        _newFolderAction = newFolderAction;
        _renameAction = renameAction;
        _newRequestAction = newRequestAction;
        _exportNativeAction = exportNativeAction;
        _exportPostmanAction = exportPostmanAction;
    }

    [RelayCommand]
    private void Open() => _openAction?.Invoke(this);

    [RelayCommand]
    private void Delete() => _deleteAction?.Invoke(this);

    [RelayCommand]
    private void NewFolder() => _newFolderAction?.Invoke(this);

    [RelayCommand]
    private void Rename() => _renameAction?.Invoke(this);

    [RelayCommand]
    private void NewRequest() => _newRequestAction?.Invoke(this);

    [RelayCommand]
    private void ExportNative() => _exportNativeAction?.Invoke(this);

    [RelayCommand]
    private void ExportPostman() => _exportPostmanAction?.Invoke(this);
}
