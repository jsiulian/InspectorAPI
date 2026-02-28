namespace InspectorAPI.Core.Models;

public class SavedRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Request";
    public HttpRequestModel Request { get; set; } = new();
    public HttpResponseModel? LastResponse { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
