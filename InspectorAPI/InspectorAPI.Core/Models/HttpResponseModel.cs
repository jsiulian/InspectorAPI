namespace InspectorAPI.Core.Models;

public class HttpResponseModel
{
    public int StatusCode { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];
    public string Body { get; set; } = string.Empty;
    public long ElapsedMilliseconds { get; set; }
    public long BodySizeBytes { get; set; }
    public bool IsSuccess => StatusCode is >= 200 and < 300;
    public string? ErrorMessage { get; set; }
}
