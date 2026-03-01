using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InspectorAPI.Core.Models;
using InspectorAPI.Core.Services;

namespace InspectorAPI.Core.ViewModels;

public partial class RequestTabViewModel : ViewModelBase
{
    private readonly IHttpRequestService _httpRequestService;
    private readonly Action<RequestTabViewModel> _closeAction;
    private readonly Action<RequestTabViewModel>? _activateAction;
    private readonly Action<RequestTabViewModel>? _saveDialogAction;
    private CancellationTokenSource? _cts;

    public static readonly string[] HttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];
    public static readonly string[] ContentTypes =
    [
        "application/json",
        "application/xml",
        "text/plain",
        "application/x-www-form-urlencoded",
        "text/html"
    ];

    // Request fields
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private string _name = "New Request";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private string _url = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private string _selectedMethod = "GET";
    [ObservableProperty] private string _bodyContent = string.Empty;
    [ObservableProperty] private string _selectedBodyContentType = "application/json";

    // State
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _hasResponse;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSelected;

    // Response fields
    [ObservableProperty] private string _responseStatus = string.Empty;
    [ObservableProperty] private string _responseStatusColor = "#666666";
    [ObservableProperty] private string _responseTime = string.Empty;
    [ObservableProperty] private string _responseSize = string.Empty;
    [ObservableProperty] private string _responseBody = string.Empty;
    [ObservableProperty] private string _responseRawBody = string.Empty;

    // Saved request tracking
    public Guid? SavedRequestId { get; private set; }
    public Guid? SavedCollectionId { get; private set; }
    public Guid? SavedFolderId { get; private set; }

    public ObservableCollection<HeaderItemViewModel> Headers { get; } = [];
    public ObservableCollection<HeaderItemViewModel> QueryParams { get; } = [];
    public ObservableCollection<HeaderItemViewModel> ResponseHeaders { get; } = [];

    public string TabTitle
    {
        get
        {
            if (SavedRequestId.HasValue) return Name;
            if (string.IsNullOrWhiteSpace(Url)) return Name;
            return Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : Url;
        }
    }

    public RequestTabViewModel(
        IHttpRequestService httpRequestService,
        Action<RequestTabViewModel> closeAction,
        Action<RequestTabViewModel>? activateAction = null,
        Action<RequestTabViewModel>? saveDialogAction = null)
    {
        _httpRequestService = httpRequestService;
        _closeAction = closeAction;
        _activateAction = activateAction;
        _saveDialogAction = saveDialogAction;
    }

    // Load from a saved request
    public void LoadFromSavedRequest(SavedRequest saved, Guid collectionId, Guid? folderId)
    {
        SavedRequestId = saved.Id;
        SavedCollectionId = collectionId;
        SavedFolderId = folderId;

        Name = saved.Name;
        Url = saved.Request.Url;
        SelectedMethod = saved.Request.Method;
        BodyContent = saved.Request.Body;
        SelectedBodyContentType = saved.Request.BodyContentType;

        Headers.Clear();
        foreach (var h in saved.Request.Headers)
            Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = h.Key, Value = h.Value, IsEnabled = h.IsEnabled });

        QueryParams.Clear();
        foreach (var p in saved.Request.QueryParams)
            QueryParams.Add(new HeaderItemViewModel(p => QueryParams.Remove(p)) { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled });

        IsDirty = false;
    }

    public HttpRequestModel ToRequestModel() => new()
    {
        Url = Url,
        Method = SelectedMethod,
        Body = BodyContent,
        BodyContentType = SelectedBodyContentType,
        Headers = Headers
            .Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key))
            .Select(h => new HeaderItem { Key = h.Key, Value = h.Value, IsEnabled = h.IsEnabled })
            .ToList(),
        QueryParams = QueryParams
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => new HeaderItem { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled })
            .ToList()
    };

    [RelayCommand]
    private async Task SendRequest()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsSending = true;
        HasResponse = false;
        ResponseStatus = string.Empty;
        ResponseBody = string.Empty;
        ResponseRawBody = string.Empty;
        ResponseHeaders.Clear();

        try
        {
            var request = ToRequestModel();
            var response = await _httpRequestService.SendAsync(request, _cts.Token);

            if (response.ErrorMessage is not null)
            {
                ResponseStatus = $"Error: {response.ErrorMessage}";
                ResponseStatusColor = "#CC0000";
                ResponseBody = response.ErrorMessage;
            }
            else
            {
                ResponseStatus = $"{response.StatusCode} {response.StatusText}";
                ResponseStatusColor = response.IsSuccess ? "#00875A" : "#CC0000";
                ResponseTime = $"{response.ElapsedMilliseconds} ms";
                ResponseSize = FormatSize(response.BodySizeBytes);
                ResponseRawBody = BuildRawView(response);
                ResponseBody = TryPrettyPrintJson(response.Body);

                ResponseHeaders.Clear();
                foreach (var h in response.Headers)
                    ResponseHeaders.Add(new HeaderItemViewModel { Key = h.Key, Value = h.Value });
            }
            HasResponse = true;
        }
        catch (OperationCanceledException) { /* user cancelled */ }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void CancelRequest()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void AddHeader() => Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)));

    [RelayCommand]
    private void AddQueryParam() => QueryParams.Add(new HeaderItemViewModel(p => QueryParams.Remove(p)));

    [RelayCommand]
    private void Close() => _closeAction(this);

    [RelayCommand]
    private void Activate() => _activateAction?.Invoke(this);

    [RelayCommand]
    private void OpenSaveDialog() => _saveDialogAction?.Invoke(this);

    private string BuildRawView(InspectorAPI.Core.Models.HttpResponseModel response)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"HTTP/1.1 {response.StatusCode} {response.StatusText}");
        foreach (var h in response.Headers)
            sb.AppendLine($"{h.Key}: {h.Value}");
        sb.AppendLine();
        sb.Append(response.Body);
        return sb.ToString();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024):F1} MB"
    };

    private static string TryPrettyPrintJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        try
        {
            var doc = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return text;
        }
    }
}
