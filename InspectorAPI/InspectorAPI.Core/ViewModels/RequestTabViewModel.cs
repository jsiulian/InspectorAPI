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
    private readonly Action<RequestTabViewModel>? _duplicateAction;
    private CancellationTokenSource? _cts;

    // Prevents re-entrant URL ↔ QueryParams sync
    private bool _syncingUrl;

    public static readonly string[] HttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];
    public static readonly string[] ContentTypes =
    [
        "application/json",
        "application/xml",
        "text/plain",
        "application/x-www-form-urlencoded",
        "text/html"
    ];
    public static readonly string[] CommonHeaders =
    [
        "Accept",
        "Accept-Charset",
        "Accept-Encoding",
        "Accept-Language",
        "Authorization",
        "Cache-Control",
        "Connection",
        "Content-Encoding",
        "Content-Length",
        "Content-Type",
        "Cookie",
        "Host",
        "If-Match",
        "If-Modified-Since",
        "If-None-Match",
        "Origin",
        "Pragma",
        "Referer",
        "Transfer-Encoding",
        "User-Agent",
        "X-Api-Key",
        "X-Auth-Token",
        "X-Correlation-ID",
        "X-Forwarded-For",
        "X-Request-ID",
        "X-Requested-With",
    ];

    // Request fields
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private string _name = "New Request";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    [NotifyPropertyChangedFor(nameof(RequestRaw))]
    private string _url = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    [NotifyPropertyChangedFor(nameof(RequestRaw))]
    private string _selectedMethod = "GET";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestRaw))]
    private string _bodyContent = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestRaw))]
    private string _selectedBodyContentType = "application/json";

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
            var qIdx = Url.IndexOf('?');
            var baseUrl = qIdx >= 0 ? Url[..qIdx] : Url;
            return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : baseUrl;
        }
    }

    public RequestTabViewModel(
        IHttpRequestService httpRequestService,
        Action<RequestTabViewModel> closeAction,
        Action<RequestTabViewModel>? activateAction = null,
        Action<RequestTabViewModel>? saveDialogAction = null,
        Action<RequestTabViewModel>? duplicateAction = null)
    {
        _httpRequestService = httpRequestService;
        _closeAction = closeAction;
        _activateAction = activateAction;
        _saveDialogAction = saveDialogAction;
        _duplicateAction = duplicateAction;

        Headers.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (HeaderItemViewModel h in e.NewItems)
                    h.PropertyChanged += (_, _) => OnPropertyChanged(nameof(RequestRaw));
            OnPropertyChanged(nameof(RequestRaw));
        };
        QueryParams.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (HeaderItemViewModel p in e.NewItems)
                {
                    p.PropertyChanged += (_, _) => OnPropertyChanged(nameof(RequestRaw));
                    p.PropertyChanged += (_, _) => SyncUrlFromParams();
                }
            OnPropertyChanged(nameof(RequestRaw));
            SyncUrlFromParams();
        };

        // Default headers for new tabs
        Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = "Accept", Value = "application/json" });
        Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = "User-Agent", Value = "InspectorAPI" });
    }

    // Called by the toolkit when Url changes — syncs query params panel from the new URL
    partial void OnUrlChanged(string value)
    {
        if (_syncingUrl) return;
        _syncingUrl = true;
        try { SyncParamsFromUrl(value); }
        finally { _syncingUrl = false; }
    }

    // Merges the query string in 'url' into the QueryParams collection.
    // Enabled params are updated to match; disabled params are left alone.
    private void SyncParamsFromUrl(string url)
    {
        var qIdx = url.IndexOf('?');
        var query = qIdx >= 0 ? url[(qIdx + 1)..] : string.Empty;

        var urlParams = string.IsNullOrEmpty(query)
            ? []
            : query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                   .Select(p =>
                   {
                       var eq = p.IndexOf('=');
                       return eq >= 0
                           ? (Key: Uri.UnescapeDataString(p[..eq]), Value: Uri.UnescapeDataString(p[(eq + 1)..]))
                           : (Key: Uri.UnescapeDataString(p), Value: "");
                   }).ToList();

        var urlParamKeys = urlParams.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove enabled params no longer present in the URL
        for (int i = QueryParams.Count - 1; i >= 0; i--)
        {
            var param = QueryParams[i];
            if (param.IsEnabled && !urlParamKeys.Contains(param.Key))
                QueryParams.RemoveAt(i);
        }

        // Update existing or add new params from URL
        foreach (var (key, value) in urlParams)
        {
            var existing = QueryParams.FirstOrDefault(p =>
                p.IsEnabled && string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                existing.Value = value;
            else
                QueryParams.Add(new HeaderItemViewModel(p => QueryParams.Remove(p)) { Key = key, Value = value, IsEnabled = true });
        }
    }

    // Rebuilds the URL query string from the currently enabled QueryParams.
    private void SyncUrlFromParams()
    {
        if (_syncingUrl) return;
        _syncingUrl = true;
        try
        {
            var qIdx = Url.IndexOf('?');
            var baseUrl = qIdx >= 0 ? Url[..qIdx] : Url;

            var enabled = QueryParams.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
            Url = enabled.Count == 0
                ? baseUrl
                : baseUrl + "?" + string.Join("&", enabled.Select(
                    p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value ?? "")));
        }
        finally { _syncingUrl = false; }
    }

    public string RequestRaw
    {
        get
        {
            var sb = new System.Text.StringBuilder();

            // Url already contains the enabled query params (kept in sync)
            if (Uri.TryCreate(Url, UriKind.Absolute, out var parsedUri))
            {
                var requestTarget = parsedUri.PathAndQuery;
                if (string.IsNullOrEmpty(requestTarget)) requestTarget = "/";
                sb.AppendLine($"{SelectedMethod} {requestTarget} HTTP/1.1");
                sb.AppendLine($"Host: {parsedUri.Host}{(parsedUri.IsDefaultPort ? "" : ":" + parsedUri.Port)}");
            }
            else
            {
                sb.AppendLine($"{SelectedMethod} {Url} HTTP/1.1");
            }

            if (!string.IsNullOrWhiteSpace(BodyContent))
                sb.AppendLine($"Content-Type: {SelectedBodyContentType}");

            foreach (var h in Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
                sb.AppendLine($"{h.Key}: {h.Value}");

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(BodyContent))
                sb.Append(BodyContent);

            return sb.ToString();
        }
    }

    // Load from a saved request
    public void LoadFromSavedRequest(SavedRequest saved, Guid collectionId, Guid? folderId)
    {
        _syncingUrl = true;
        try
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
        finally
        {
            _syncingUrl = false;
        }

        // Rebuild URL from enabled params (saved URL may be base-only)
        SyncUrlFromParams();
    }

    // Copies all request fields from another tab (used for duplication).
    public void CopyFrom(RequestTabViewModel src)
    {
        _syncingUrl = true;
        try
        {
            Name = $"Copy of {src.Name}";
            Url = src.Url;
            SelectedMethod = src.SelectedMethod;
            BodyContent = src.BodyContent;
            SelectedBodyContentType = src.SelectedBodyContentType;

            Headers.Clear();
            foreach (var h in src.Headers)
                Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = h.Key, Value = h.Value, IsEnabled = h.IsEnabled });

            QueryParams.Clear();
            foreach (var p in src.QueryParams)
                QueryParams.Add(new HeaderItemViewModel(p => QueryParams.Remove(p)) { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled });
        }
        finally
        {
            _syncingUrl = false;
        }
    }

    // Records that this tab's content has been persisted (prevents duplicate saves).
    public void MarkAsSaved(Guid requestId, Guid collectionId, Guid? folderId)
    {
        SavedRequestId = requestId;
        SavedCollectionId = collectionId;
        SavedFolderId = folderId;
        OnPropertyChanged(nameof(TabTitle));
    }

    // For sending HTTP requests — URL already contains enabled query params.
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
        QueryParams = [] // params already embedded in Url
    };

    // For saving to disk — stores base URL + all params (including disabled) separately.
    public HttpRequestModel ToSaveModel()
    {
        var qIdx = Url.IndexOf('?');
        var baseUrl = qIdx >= 0 ? Url[..qIdx] : Url;
        return new HttpRequestModel
        {
            Url = baseUrl,
            Method = SelectedMethod,
            Body = BodyContent,
            BodyContentType = SelectedBodyContentType,
            Headers = Headers
                .Select(h => new HeaderItem { Key = h.Key, Value = h.Value, IsEnabled = h.IsEnabled })
                .ToList(),
            QueryParams = QueryParams
                .Select(p => new HeaderItem { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled })
                .ToList()
        };
    }

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

    [RelayCommand]
    private void Duplicate() => _duplicateAction?.Invoke(this);

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
