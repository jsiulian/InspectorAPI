namespace InspectorAPI.Core.Models;

public class Collection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Collection";
    public string Description { get; set; } = string.Empty;
    public List<CollectionFolder> Folders { get; set; } = [];
    public List<SavedRequest> Requests { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
