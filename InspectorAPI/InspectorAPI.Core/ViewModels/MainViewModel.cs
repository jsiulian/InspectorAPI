using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InspectorAPI.Core.Models;
using InspectorAPI.Core.Services;

namespace InspectorAPI.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ICollectionService _collectionService;
    private readonly IHttpRequestService _httpRequestService;

    [ObservableProperty] private ObservableCollection<RequestTabViewModel> _tabs = [];
    [ObservableProperty] private RequestTabViewModel? _selectedTab;
    [ObservableProperty] private ObservableCollection<CollectionTreeNodeViewModel> _collectionTree = [];
    [ObservableProperty] private bool _isLoadingCollections;

    // Save dialog state
    [ObservableProperty] private bool _isSaveDialogOpen;
    [ObservableProperty] private string _saveRequestName = string.Empty;
    [ObservableProperty] private CollectionTreeNodeViewModel? _selectedSaveTarget;
    public ObservableCollection<CollectionTreeNodeViewModel> FlatSaveTargets { get; } = [];

    // Name dialog state (used for new collection, new folder, rename)
    [ObservableProperty] private bool _isNameDialogOpen;
    [ObservableProperty] private string _nameDialogTitle = string.Empty;
    [ObservableProperty] private string _nameDialogValue = string.Empty;
    private Func<string, Task>? _pendingNameAction;

    public MainViewModel(ICollectionService collectionService, IHttpRequestService httpRequestService)
    {
        _collectionService = collectionService;
        _httpRequestService = httpRequestService;
    }

    public async Task InitializeAsync()
    {
        await LoadCollectionsAsync();
        AddNewTab();
    }

    [RelayCommand]
    private void AddNewTab()
    {
        var tab = new RequestTabViewModel(_httpRequestService, CloseTab, t => SelectedTab = t, OpenSaveDialogForTab);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    partial void OnSelectedTabChanged(RequestTabViewModel? value)
    {
        foreach (var tab in Tabs)
            tab.IsSelected = tab == value;
    }

    private void OpenSaveDialogForTab(RequestTabViewModel tab)
    {
        SelectedTab = tab;
        SaveRequestName = tab.Name;
        RebuildFlatSaveTargets();
        IsSaveDialogOpen = true;
    }

    private void RebuildFlatSaveTargets()
    {
        FlatSaveTargets.Clear();
        foreach (var col in CollectionTree)
        {
            col.FlatDepth = 0;
            FlatSaveTargets.Add(col);
            AddFlatFolders(col.Children, 1);
        }
    }

    private void AddFlatFolders(ObservableCollection<CollectionTreeNodeViewModel> nodes, int depth)
    {
        foreach (var node in nodes.Where(n => n.NodeType != CollectionNodeType.Request))
        {
            node.FlatDepth = depth;
            FlatSaveTargets.Add(node);
            AddFlatFolders(node.Children, depth + 1);
        }
    }

    private void CloseTab(RequestTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count > 0)
            SelectedTab = Tabs[Math.Max(0, index - 1)];
    }

    [RelayCommand]
    private void NewCollection()
    {
        _pendingNameAction = async name =>
        {
            var collection = new Collection { Name = name };
            await _collectionService.SaveCollectionAsync(collection);
            CollectionTree.Add(BuildCollectionNode(collection));
        };
        NameDialogTitle = "New Collection";
        NameDialogValue = string.Empty;
        IsNameDialogOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmNameDialog()
    {
        if (_pendingNameAction is not null && !string.IsNullOrWhiteSpace(NameDialogValue))
            await _pendingNameAction(NameDialogValue);
        IsNameDialogOpen = false;
        _pendingNameAction = null;
    }

    [RelayCommand]
    private void CancelNameDialog()
    {
        IsNameDialogOpen = false;
        _pendingNameAction = null;
    }

    [RelayCommand]
    private async Task SaveRequest()
    {
        if (SelectedTab is null || SelectedSaveTarget is null) return;

        var collectionId = GetCollectionId(SelectedSaveTarget);
        var folderId = SelectedSaveTarget.NodeType == CollectionNodeType.Folder
            ? SelectedSaveTarget.Folder?.Id : null;

        var saved = new SavedRequest
        {
            Id = SelectedTab.SavedRequestId ?? Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(SaveRequestName) ? SelectedTab.TabTitle : SaveRequestName,
            Request = SelectedTab.ToRequestModel()
        };

        if (SelectedTab.SavedRequestId.HasValue)
            await _collectionService.UpdateRequestAsync(collectionId, folderId, saved);
        else
            await _collectionService.AddRequestToCollectionAsync(collectionId, folderId, saved);

        SelectedTab.Name = saved.Name;

        // Add to tree if new
        if (!SelectedTab.SavedRequestId.HasValue)
        {
            var reqNode = BuildRequestNode(saved, collectionId, folderId);
            SelectedSaveTarget.Children.Add(reqNode);
            SelectedSaveTarget.IsExpanded = true;
        }

        IsSaveDialogOpen = false;
    }

    [RelayCommand]
    private void CancelSaveDialog() => IsSaveDialogOpen = false;

    private async Task LoadCollectionsAsync()
    {
        IsLoadingCollections = true;
        try
        {
            var collections = await _collectionService.LoadAllAsync();
            CollectionTree.Clear();
            foreach (var col in collections)
                CollectionTree.Add(BuildCollectionNode(col));
        }
        finally
        {
            IsLoadingCollections = false;
        }
    }

    private CollectionTreeNodeViewModel BuildCollectionNode(Collection col)
    {
        var node = new CollectionTreeNodeViewModel
        {
            Name = col.Name,
            NodeType = CollectionNodeType.Collection,
            Collection = col,
            ParentCollectionId = col.Id,
            IsExpanded = false
        };
        node.SetActions(null, n => _ = DeleteNodeAsync(n), n => NewFolderOnNode(n), n => RenameNode(n), n => NewRequestOnNode(n));

        foreach (var folder in col.Folders)
            node.Children.Add(BuildFolderNode(folder, col.Id, null));

        foreach (var req in col.Requests)
            node.Children.Add(BuildRequestNode(req, col.Id, null));

        return node;
    }

    private CollectionTreeNodeViewModel BuildFolderNode(CollectionFolder folder, Guid collectionId, Guid? parentFolderId)
    {
        var node = new CollectionTreeNodeViewModel
        {
            Name = folder.Name,
            NodeType = CollectionNodeType.Folder,
            Folder = folder,
            ParentCollectionId = collectionId,
            ParentFolderId = parentFolderId,
            IsExpanded = false
        };
        node.SetActions(null, n => _ = DeleteNodeAsync(n), n => NewFolderOnNode(n), n => RenameNode(n), n => NewRequestOnNode(n));

        foreach (var sub in folder.Folders)
            node.Children.Add(BuildFolderNode(sub, collectionId, folder.Id));

        foreach (var req in folder.Requests)
            node.Children.Add(BuildRequestNode(req, collectionId, folder.Id));

        return node;
    }

    private CollectionTreeNodeViewModel BuildRequestNode(SavedRequest req, Guid collectionId, Guid? folderId)
    {
        var node = new CollectionTreeNodeViewModel
        {
            Name = req.Name,
            NodeType = CollectionNodeType.Request,
            SavedRequest = req,
            ParentCollectionId = collectionId,
            ParentFolderId = folderId
        };
        node.SetActions(OpenRequestInTab, n => _ = DeleteNodeAsync(n), null, n => RenameNode(n));
        return node;
    }

    private void OpenRequestInTab(CollectionTreeNodeViewModel node)
    {
        if (node.SavedRequest is null) return;
        var existing = Tabs.FirstOrDefault(t => t.SavedRequestId == node.SavedRequest.Id);
        if (existing is not null) { SelectedTab = existing; return; }
        var tab = new RequestTabViewModel(_httpRequestService, CloseTab, t => SelectedTab = t, OpenSaveDialogForTab);
        tab.LoadFromSavedRequest(node.SavedRequest, node.ParentCollectionId!.Value, node.ParentFolderId);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private async Task DeleteNodeAsync(CollectionTreeNodeViewModel node)
    {
        if (node.NodeType == CollectionNodeType.Collection && node.Collection is not null)
        {
            await _collectionService.DeleteCollectionAsync(node.Collection.Id);
            CollectionTree.Remove(node);
        }
        else if (node.NodeType == CollectionNodeType.Folder && node.Folder is not null && node.ParentCollectionId is not null)
        {
            await _collectionService.DeleteFolderAsync(node.ParentCollectionId.Value, node.ParentFolderId, node.Folder.Id);
            RemoveNodeFromTree(CollectionTree, node);
        }
        else if (node.NodeType == CollectionNodeType.Request && node.SavedRequest is not null && node.ParentCollectionId is not null)
        {
            await _collectionService.DeleteRequestAsync(node.ParentCollectionId.Value, node.ParentFolderId, node.SavedRequest.Id);
            RemoveNodeFromTree(CollectionTree, node);
            var openTab = Tabs.FirstOrDefault(t => t.SavedRequestId == node.SavedRequest.Id);
            if (openTab is not null) CloseTab(openTab);
        }
    }

    private void NewFolderOnNode(CollectionTreeNodeViewModel parent)
    {
        _pendingNameAction = async name =>
        {
            var folder = new CollectionFolder { Name = name };
            var collectionId = GetCollectionId(parent);
            var folderId = parent.NodeType == CollectionNodeType.Folder ? parent.Folder?.Id : null;
            await _collectionService.AddFolderAsync(collectionId, folderId, folder);
            parent.Children.Add(BuildFolderNode(folder, collectionId, folderId));
            parent.IsExpanded = true;
        };
        NameDialogTitle = "New Folder";
        NameDialogValue = string.Empty;
        IsNameDialogOpen = true;
    }

    private void NewRequestOnNode(CollectionTreeNodeViewModel parent)
    {
        var tab = new RequestTabViewModel(_httpRequestService, CloseTab, t => SelectedTab = t, OpenSaveDialogForTab);
        Tabs.Add(tab);
        SelectedTab = tab;

        // Pre-open the save dialog with this collection/folder already selected
        SaveRequestName = string.Empty;
        RebuildFlatSaveTargets();
        SelectedSaveTarget = FlatSaveTargets.FirstOrDefault(n => n == parent);
        IsSaveDialogOpen = true;
    }

    private void RenameNode(CollectionTreeNodeViewModel node)
    {
        _pendingNameAction = async name =>
        {
            node.Name = name;
            if (node.NodeType == CollectionNodeType.Collection && node.Collection is not null)
            {
                node.Collection.Name = name;
                await _collectionService.SaveCollectionAsync(node.Collection);
            }
            else if (node.NodeType == CollectionNodeType.Folder && node.Folder is not null && node.ParentCollectionId is not null)
            {
                await _collectionService.RenameFolderAsync(node.ParentCollectionId.Value, node.Folder.Id, name);
            }
            else if (node.NodeType == CollectionNodeType.Request && node.SavedRequest is not null && node.ParentCollectionId is not null)
            {
                await _collectionService.RenameRequestAsync(node.ParentCollectionId.Value, node.ParentFolderId, node.SavedRequest.Id, name);
                var openTab = Tabs.FirstOrDefault(t => t.SavedRequestId == node.SavedRequest.Id);
                if (openTab is not null) openTab.Name = name;
            }
        };
        NameDialogTitle = node.NodeType switch
        {
            CollectionNodeType.Collection => "Rename Collection",
            CollectionNodeType.Folder => "Rename Folder",
            _ => "Rename Request"
        };
        NameDialogValue = node.Name;
        IsNameDialogOpen = true;
    }

    private static Guid GetCollectionId(CollectionTreeNodeViewModel node) =>
        node.ParentCollectionId ?? node.Collection?.Id ?? Guid.Empty;

    private static bool RemoveNodeFromTree(ObservableCollection<CollectionTreeNodeViewModel> nodes, CollectionTreeNodeViewModel target)
    {
        if (nodes.Remove(target)) return true;
        foreach (var node in nodes)
        {
            if (RemoveNodeFromTree(node.Children, target)) return true;
        }
        return false;
    }
}
