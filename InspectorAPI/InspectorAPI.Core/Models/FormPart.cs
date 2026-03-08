namespace InspectorAPI.Core.Models;

public class FormPart
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
