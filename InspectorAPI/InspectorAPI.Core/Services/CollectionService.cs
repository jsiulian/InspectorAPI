using System.Text.Json;
using InspectorAPI.Core.Models;

namespace InspectorAPI.Core.Services;

public class CollectionService : ICollectionService
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InspectorAPI", "collections");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CollectionService()
    {
        Directory.CreateDirectory(StorageDir);
    }

    public async Task<List<Collection>> LoadAllAsync()
    {
        var collections = new List<Collection>();
        foreach (var file in Directory.GetFiles(StorageDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var col = JsonSerializer.Deserialize<Collection>(json, JsonOptions);
                if (col is not null) collections.Add(col);
            }
            catch { /* skip corrupt files */ }
        }
        return collections.OrderBy(c => c.Name).ToList();
    }

    public async Task SaveCollectionAsync(Collection collection)
    {
        collection.UpdatedAt = DateTime.UtcNow;
        var file = Path.Combine(StorageDir, $"{collection.Id}.json");
        var json = JsonSerializer.Serialize(collection, JsonOptions);
        await File.WriteAllTextAsync(file, json);
    }

    public Task DeleteCollectionAsync(Guid collectionId)
    {
        var file = Path.Combine(StorageDir, $"{collectionId}.json");
        if (File.Exists(file)) File.Delete(file);
        return Task.CompletedTask;
    }

    public async Task AddRequestToCollectionAsync(Guid collectionId, Guid? folderId, SavedRequest request)
    {
        var collections = await LoadAllAsync();
        var col = collections.FirstOrDefault(c => c.Id == collectionId);
        if (col is null) return;

        if (folderId is null)
        {
            col.Requests.Add(request);
        }
        else
        {
            var folder = FindFolder(col, folderId.Value);
            folder?.Requests.Add(request);
        }
        await SaveCollectionAsync(col);
    }

    public async Task UpdateRequestAsync(Guid collectionId, Guid? folderId, SavedRequest request)
    {
        var collections = await LoadAllAsync();
        var col = collections.FirstOrDefault(c => c.Id == collectionId);
        if (col is null) return;

        request.UpdatedAt = DateTime.UtcNow;

        List<SavedRequest> list = folderId is null
            ? col.Requests
            : FindFolder(col, folderId.Value)?.Requests ?? col.Requests;

        var idx = list.FindIndex(r => r.Id == request.Id);
        if (idx >= 0) list[idx] = request;

        await SaveCollectionAsync(col);
    }

    public async Task DeleteRequestAsync(Guid collectionId, Guid? folderId, Guid requestId)
    {
        var collections = await LoadAllAsync();
        var col = collections.FirstOrDefault(c => c.Id == collectionId);
        if (col is null) return;

        List<SavedRequest> list = folderId is null
            ? col.Requests
            : FindFolder(col, folderId.Value)?.Requests ?? col.Requests;

        list.RemoveAll(r => r.Id == requestId);
        await SaveCollectionAsync(col);
    }

    public async Task AddFolderAsync(Guid collectionId, Guid? parentFolderId, CollectionFolder folder)
    {
        var collections = await LoadAllAsync();
        var col = collections.FirstOrDefault(c => c.Id == collectionId);
        if (col is null) return;

        if (parentFolderId is null)
            col.Folders.Add(folder);
        else
            FindFolder(col, parentFolderId.Value)?.Folders.Add(folder);

        await SaveCollectionAsync(col);
    }

    public async Task DeleteFolderAsync(Guid collectionId, Guid? parentFolderId, Guid folderId)
    {
        var collections = await LoadAllAsync();
        var col = collections.FirstOrDefault(c => c.Id == collectionId);
        if (col is null) return;
        var parentList = parentFolderId is null
            ? col.Folders
            : FindFolder(col, parentFolderId.Value)?.Folders;
        parentList?.RemoveAll(f => f.Id == folderId);
        await SaveCollectionAsync(col);
    }

    public async Task RenameFolderAsync(Guid collectionId, Guid folderId, string newName)
    {
        var collections = await LoadAllAsync();
        var col = collections.FirstOrDefault(c => c.Id == collectionId);
        if (col is null) return;
        var folder = FindFolder(col, folderId);
        if (folder is null) return;
        folder.Name = newName;
        await SaveCollectionAsync(col);
    }

    public async Task RenameRequestAsync(Guid collectionId, Guid? folderId, Guid requestId, string newName)
    {
        var collections = await LoadAllAsync();
        var col = collections.FirstOrDefault(c => c.Id == collectionId);
        if (col is null) return;
        var list = folderId is null
            ? col.Requests
            : FindFolder(col, folderId.Value)?.Requests ?? col.Requests;
        var req = list.FirstOrDefault(r => r.Id == requestId);
        if (req is null) return;
        req.Name = newName;
        await SaveCollectionAsync(col);
    }

    private static CollectionFolder? FindFolder(Collection col, Guid folderId)
    {
        return FindFolderRecursive(col.Folders, folderId);
    }

    private static CollectionFolder? FindFolderRecursive(List<CollectionFolder> folders, Guid id)
    {
        foreach (var f in folders)
        {
            if (f.Id == id) return f;
            var found = FindFolderRecursive(f.Folders, id);
            if (found is not null) return found;
        }
        return null;
    }

    public async Task<Collection?> ImportFromJsonAsync(string json)
    {
        Collection? col;
        if (json.Contains("schema.getpostman.com"))
            col = PostmanConverter.FromPostman(json);
        else
            col = JsonSerializer.Deserialize<Collection>(json, JsonOptions);

        if (col is null) return null;

        var imported = RegenerateIds(col);
        await SaveCollectionAsync(imported);
        return imported;
    }

    public async Task ExportToFileAsync(Guid collectionId, string path, bool asPostman)
    {
        if (asPostman)
        {
            var col = await GetCollectionAsync(collectionId);
            if (col is null) return;
            var json = PostmanConverter.ToPostman(col);
            await File.WriteAllTextAsync(path, json);
        }
        else
        {
            var src = Path.Combine(StorageDir, $"{collectionId}.json");
            if (File.Exists(src)) File.Copy(src, path, overwrite: true);
        }
    }

    private async Task<Collection?> GetCollectionAsync(Guid id)
    {
        var file = Path.Combine(StorageDir, $"{id}.json");
        if (!File.Exists(file)) return null;
        var json = await File.ReadAllTextAsync(file);
        return JsonSerializer.Deserialize<Collection>(json, JsonOptions);
    }

    private static Collection RegenerateIds(Collection col) => new()
    {
        Name = col.Name,
        Description = col.Description,
        Requests = col.Requests.Select(r => new SavedRequest { Name = r.Name, Request = r.Request }).ToList(),
        Folders = col.Folders.Select(RegenerateFolderIds).ToList()
    };

    private static CollectionFolder RegenerateFolderIds(CollectionFolder f) => new()
    {
        Name = f.Name,
        Requests = f.Requests.Select(r => new SavedRequest { Name = r.Name, Request = r.Request }).ToList(),
        Folders = f.Folders.Select(RegenerateFolderIds).ToList()
    };
}
