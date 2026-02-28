using InspectorAPI.Core.Models;

namespace InspectorAPI.Core.Services;

public interface ICollectionService
{
    Task<List<Collection>> LoadAllAsync();
    Task SaveCollectionAsync(Collection collection);
    Task DeleteCollectionAsync(Guid collectionId);
    Task AddRequestToCollectionAsync(Guid collectionId, Guid? folderId, SavedRequest request);
    Task UpdateRequestAsync(Guid collectionId, Guid? folderId, SavedRequest request);
    Task DeleteRequestAsync(Guid collectionId, Guid? folderId, Guid requestId);
    Task AddFolderAsync(Guid collectionId, Guid? parentFolderId, CollectionFolder folder);
}
