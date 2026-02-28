namespace InspectorAPI.Core.Models;

public class CollectionFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Folder";
    public List<SavedRequest> Requests { get; set; } = [];
    public List<CollectionFolder> Folders { get; set; } = [];
}
