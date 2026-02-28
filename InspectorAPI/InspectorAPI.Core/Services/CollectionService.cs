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
}
