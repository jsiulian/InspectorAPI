namespace InspectorAPI.Core.Models;

public class HttpRequestModel
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public List<HeaderItem> Headers { get; set; } = [];
    public List<HeaderItem> QueryParams { get; set; } = [];
    public string Body { get; set; } = string.Empty;
    public string BodyContentType { get; set; } = "application/json";
}
