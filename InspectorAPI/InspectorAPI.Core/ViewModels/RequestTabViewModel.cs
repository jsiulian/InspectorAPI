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

    // True while change came FROM the header (or during load/copy) — suppresses header sync and auto-populate
    private bool _syncingContentType;

    // True while reverting SelectedBodyContentType after user cancels the body-clear dialog
    private bool _revertingBodyType;

    // True while rebuilding raw from fields, or while parsing raw into fields — prevents loops
    private bool _syncingRaw;

    // Set permanently when Close() is called — suppresses any binding write-backs during DataTemplate teardown
    private bool _isClosing;

    // The content type to revert to if user cancels the body-clear dialog
    private string _previousBodyContentType = "application/json";

    // Tracks the single custom Content-Type value injected into BodyContentTypes (prevents accumulation)
    private string? _customBodyContentType;

    public static readonly string[] HttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];
    public static readonly string[] ContentTypes =
    [
        "application/json",
        "application/xml",
        "text/plain",
        "application/x-www-form-urlencoded",
        "multipart/form-data",
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
    private string _url = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private string _selectedMethod = "GET";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRawBody))]
    private string _bodyContent = string.Empty;

    [ObservableProperty]
    private string _selectedBodyContentType = "application/json";

    // The editable raw HTTP request text — kept in sync with all other fields
    [ObservableProperty]
    private string _requestRaw = string.Empty;

    // State
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _hasResponse;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isBodyClearConfirmDialogOpen;

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

    // Standard types + at most ONE injected custom type (prevents dropdown accumulation)
    public ObservableCollection<string> BodyContentTypes { get; } = new(ContentTypes);

    // Form body collections
    public ObservableCollection<HeaderItemViewModel> FormParams { get; } = [];
    public ObservableCollection<FormPartViewModel> FormParts { get; } = [];

    // Computed body-type switches (used in AXAML without converters)
    public bool IsFormUrlEncodedBody => SelectedBodyContentType == "application/x-www-form-urlencoded";
    public bool IsMultipartBody => SelectedBodyContentType == "multipart/form-data";
    // Falls back to raw when a form type is active but its collection is empty and BodyContent has text
    public bool IsRawBody =>
        (!IsFormUrlEncodedBody && !IsMultipartBody) ||
        (IsFormUrlEncodedBody && FormParams.Count == 0 && !string.IsNullOrWhiteSpace(BodyContent)) ||
        (IsMultipartBody && FormParts.Count == 0 && !string.IsNullOrWhiteSpace(BodyContent));

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
                {
                    h.PropertyChanged += (_, _) => RebuildRaw();
                    h.PropertyChanged += (_, _) => SyncBodyContentTypeFromHeader();
                }
            RebuildRaw();
            SyncBodyContentTypeFromHeader();
        };
        QueryParams.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (HeaderItemViewModel p in e.NewItems)
                {
                    p.PropertyChanged += (_, _) => RebuildRaw();
                    p.PropertyChanged += (_, _) => SyncUrlFromParams();
                }
            RebuildRaw();
            SyncUrlFromParams();
        };
        FormParams.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (HeaderItemViewModel p in e.NewItems)
                    p.PropertyChanged += (_, _) => RebuildRaw();
            RebuildRaw();
            OnPropertyChanged(nameof(IsRawBody));
        };
        FormParts.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (FormPartViewModel p in e.NewItems)
                    p.PropertyChanged += (_, _) => RebuildRaw();
            RebuildRaw();
            OnPropertyChanged(nameof(IsRawBody));
        };

        // Default headers for new tabs
        Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = "Accept", Value = "application/json" });
        Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = "User-Agent", Value = "InspectorAPI" });
    }

    // -------------------------------------------------------------------------
    // Raw ↔ fields sync
    // -------------------------------------------------------------------------

    // User edited the Raw text area — parse and push into all other fields.
    partial void OnRequestRawChanged(string value)
    {
        if (_syncingRaw) return;
        _syncingRaw = true;
        try { ParseAndApplyRaw(value); }
        finally { _syncingRaw = false; }
    }

    // Any field changed — recompute and push into RequestRaw.
    private void RebuildRaw()
    {
        if (_syncingRaw) return;
        _syncingRaw = true;
        try { RequestRaw = ComputeRaw(); }
        finally { _syncingRaw = false; }
    }

    // Parses a raw HTTP request string and updates all fields accordingly.
    // Silently ignores malformed input so partial edits don't destroy state.
    private void ParseAndApplyRaw(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        int i = 0;

        // --- Request line: METHOD path HTTP/version ---
        if (i >= lines.Length) return;
        var requestParts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 1 || !HttpMethods.Contains(requestParts[0]))
            return; // not a recognisable HTTP request yet — leave everything as-is

        var newMethod = requestParts[0];
        var pathAndQuery = requestParts.Length >= 2 ? requestParts[1] : null;
        i++;

        // --- Headers ---
        string? hostValue = null;
        var parsedHeaders = new List<(string Key, string Value)>();

        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
        {
            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx > 0)
            {
                var key = lines[i][..colonIdx].Trim();
                var value = lines[i][(colonIdx + 1)..].Trim();
                if (string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase))
                    hostValue = value;
                else
                    parsedHeaders.Add((key, value));
            }
            i++;
        }
        i++; // skip blank separator line

        // --- Body (everything after the blank line) ---
        var bodyText = i < lines.Length
            ? string.Join("\n", lines[i..]).TrimEnd('\r', '\n')
            : string.Empty;

        // === Apply ===

        SelectedMethod = newMethod;

        // Reconstruct URL: preserve existing scheme, replace host and path
        if (pathAndQuery != null)
        {
            var scheme = Uri.TryCreate(Url, UriKind.Absolute, out var existing) ? existing.Scheme : "https";
            if (hostValue != null)
                Url = $"{scheme}://{hostValue}{pathAndQuery}";
            else if (existing != null)
                Url = $"{scheme}://{existing.Authority}{pathAndQuery}";
        }

        // Replace headers entirely with what is in the raw text
        Headers.Clear();
        foreach (var (key, value) in parsedHeaders)
            Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = key, Value = value, IsEnabled = true });
        // (SyncBodyContentTypeFromHeader is called by the CollectionChanged handler above)

        // Apply body — respect the current content type after headers have been applied
        var multipartPlaceholder = "-- multipart/form-data";
        if (IsFormUrlEncodedBody && !string.IsNullOrWhiteSpace(bodyText))
        {
            if (TryParseUrlEncoded(bodyText, out var formPairs))
            {
                FormParams.Clear();
                foreach (var (k, v) in formPairs)
                    FormParams.Add(new HeaderItemViewModel(p => FormParams.Remove(p)) { Key = k, Value = v });
                BodyContent = string.Empty;
            }
            else
            {
                BodyContent = bodyText;
            }
        }
        else if (!IsMultipartBody || !bodyText.StartsWith(multipartPlaceholder))
        {
            BodyContent = bodyText;
        }
    }

    // Computes the canonical raw HTTP representation from all current fields.
    private string ComputeRaw()
    {
        var sb = new System.Text.StringBuilder();

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

        // Only emit Content-Type inline when it is not already in the headers list
        var hasCTHeader = Headers.Any(h => h.IsEnabled &&
            string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase));
        var hasBody = IsFormUrlEncodedBody
            ? FormParams.Any(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            : IsMultipartBody
                ? FormParts.Any(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
                : !string.IsNullOrWhiteSpace(BodyContent);
        if (!hasCTHeader && hasBody)
            sb.AppendLine($"Content-Type: {SelectedBodyContentType}");

        foreach (var h in Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
            sb.AppendLine($"{h.Key}: {h.Value}");

        sb.AppendLine();

        if (IsFormUrlEncodedBody)
        {
            sb.Append(string.Join("&", FormParams
                .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
                .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value ?? ""))));
        }
        else if (IsMultipartBody)
        {
            sb.Append("-- multipart/form-data (see Form Parts) --");
        }
        else if (!string.IsNullOrWhiteSpace(BodyContent))
        {
            sb.Append(BodyContent);
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Field change handlers that trigger RebuildRaw
    // -------------------------------------------------------------------------

    partial void OnUrlChanged(string value)
    {
        if (_syncingUrl) return;
        _syncingUrl = true;
        try { SyncParamsFromUrl(value); }
        finally { _syncingUrl = false; }
        RebuildRaw();
    }

    partial void OnSelectedMethodChanged(string value) => RebuildRaw();

    partial void OnBodyContentChanged(string value) => RebuildRaw();

    // -------------------------------------------------------------------------
    // Body content type sync
    // -------------------------------------------------------------------------

    partial void OnSelectedBodyContentTypeChanging(string value)
    {
        if (!_revertingBodyType)
            _previousBodyContentType = SelectedBodyContentType;
    }

    partial void OnSelectedBodyContentTypeChanged(string value)
    {
        bool triggeredExternally = _syncingContentType || _syncingRaw || _isClosing;

        OnPropertyChanged(nameof(IsRawBody));
        OnPropertyChanged(nameof(IsFormUrlEncodedBody));
        OnPropertyChanged(nameof(IsMultipartBody));
        RebuildRaw();

        if (_revertingBodyType) return;

        if (!triggeredExternally)
        {
            _syncingContentType = true;
            try { SyncContentTypeHeader(value); }
            finally { _syncingContentType = false; }
        }

        if (!triggeredExternally && !string.IsNullOrWhiteSpace(BodyContent))
        {
            if (value == "application/x-www-form-urlencoded")
            {
                if (TryParseUrlEncoded(BodyContent, out var parsedParams))
                {
                    FormParams.Clear();
                    foreach (var (k, v) in parsedParams)
                        FormParams.Add(new HeaderItemViewModel(p => FormParams.Remove(p)) { Key = k, Value = v });
                    BodyContent = string.Empty;
                }
                else
                {
                    IsBodyClearConfirmDialogOpen = true;
                }
            }
            else if (value == "multipart/form-data")
            {
                IsBodyClearConfirmDialogOpen = true;
            }
        }
    }

    // -------------------------------------------------------------------------
    // URL ↔ QueryParams sync
    // -------------------------------------------------------------------------

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

        for (int i = QueryParams.Count - 1; i >= 0; i--)
        {
            var param = QueryParams[i];
            if (param.IsEnabled && !urlParamKeys.Contains(param.Key))
                QueryParams.RemoveAt(i);
        }

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

    // -------------------------------------------------------------------------
    // Content-Type header ↔ body dropdown sync
    // -------------------------------------------------------------------------

    private void SyncBodyContentTypeFromHeader()
    {
        if (_syncingContentType) return;

        var ctHeader = Headers.FirstOrDefault(h => h.IsEnabled &&
            string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase));
        if (ctHeader is null) return;

        var value = ctHeader.Value ?? string.Empty;
        if (string.IsNullOrEmpty(value)) return;

        _syncingContentType = true;
        try
        {
            SetCustomBodyContentType(value);
            SelectedBodyContentType = value;
        }
        finally { _syncingContentType = false; }
    }

    private void SetCustomBodyContentType(string? value)
    {
        if (_customBodyContentType != null && _customBodyContentType != value)
        {
            BodyContentTypes.Remove(_customBodyContentType);
            _customBodyContentType = null;
        }

        if (value != null && !ContentTypes.Contains(value))
        {
            if (!BodyContentTypes.Contains(value))
                BodyContentTypes.Add(value);
            _customBodyContentType = value;
        }
        else if (value == null || ContentTypes.Contains(value))
        {
            _customBodyContentType = null;
        }
    }

    private void SyncContentTypeHeader(string contentType)
    {
        var existing = Headers.FirstOrDefault(h =>
            string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            existing.Value = contentType;
        else
            Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h))
                { Key = "Content-Type", Value = contentType, IsEnabled = true });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool TryParseUrlEncoded(string body, out List<(string Key, string Value)> pairs)
    {
        pairs = [];
        if (string.IsNullOrWhiteSpace(body)) return true;

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[') || trimmed.StartsWith('<'))
            return false;
        if (!body.Contains('='))
            return false;

        try
        {
            foreach (var segment in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = segment.IndexOf('=');
                if (eq < 0) return false;
                pairs.Add((Uri.UnescapeDataString(segment[..eq]), Uri.UnescapeDataString(segment[(eq + 1)..])));
            }
            return pairs.Count > 0;
        }
        catch { return false; }
    }

    // -------------------------------------------------------------------------
    // Load / copy / save
    // -------------------------------------------------------------------------

    public void LoadFromSavedRequest(SavedRequest saved, Guid collectionId, Guid? folderId)
    {
        _syncingUrl = true;
        _syncingContentType = true;
        _syncingRaw = true;
        try
        {
            SavedRequestId = saved.Id;
            SavedCollectionId = collectionId;
            SavedFolderId = folderId;

            Name = saved.Name;
            Url = saved.Request.Url;
            SelectedMethod = saved.Request.Method;
            BodyContent = saved.Request.Body;

            Headers.Clear();
            foreach (var h in saved.Request.Headers)
                Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = h.Key, Value = h.Value, IsEnabled = h.IsEnabled });

            QueryParams.Clear();
            foreach (var p in saved.Request.QueryParams)
                QueryParams.Add(new HeaderItemViewModel(p => QueryParams.Remove(p)) { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled });

            FormParams.Clear();
            foreach (var p in saved.Request.FormParams)
                FormParams.Add(new HeaderItemViewModel(p => FormParams.Remove(p)) { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled });

            FormParts.Clear();
            foreach (var p in saved.Request.FormParts)
                FormParts.Add(new FormPartViewModel(p => FormParts.Remove(p)) { Key = p.Key, Value = p.Value, PartContentType = p.ContentType, IsEnabled = p.IsEnabled });

            var ctHeader = saved.Request.Headers.FirstOrDefault(h =>
                string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase));
            var resolvedCT = ctHeader?.Value ?? saved.Request.BodyContentType;
            SetCustomBodyContentType(string.IsNullOrEmpty(resolvedCT) ? null : resolvedCT);
            SelectedBodyContentType = string.IsNullOrEmpty(resolvedCT) ? "application/json" : resolvedCT;

            if (SelectedBodyContentType == "application/x-www-form-urlencoded"
                && FormParams.Count == 0
                && !string.IsNullOrWhiteSpace(BodyContent)
                && TryParseUrlEncoded(BodyContent, out var migratedParams))
            {
                foreach (var (k, v) in migratedParams)
                    FormParams.Add(new HeaderItemViewModel(p => FormParams.Remove(p)) { Key = k, Value = v });
                BodyContent = string.Empty;
            }

            IsDirty = false;
        }
        finally
        {
            _syncingUrl = false;
            _syncingContentType = false;
            _syncingRaw = false;
        }

        SyncUrlFromParams();
        RebuildRaw();
    }

    public void CopyFrom(RequestTabViewModel src)
    {
        _syncingUrl = true;
        _syncingContentType = true;
        _syncingRaw = true;
        try
        {
            Name = $"Copy of {src.Name}";
            Url = src.Url;
            SelectedMethod = src.SelectedMethod;
            BodyContent = src.BodyContent;

            Headers.Clear();
            foreach (var h in src.Headers)
                Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)) { Key = h.Key, Value = h.Value, IsEnabled = h.IsEnabled });

            QueryParams.Clear();
            foreach (var p in src.QueryParams)
                QueryParams.Add(new HeaderItemViewModel(p => QueryParams.Remove(p)) { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled });

            FormParams.Clear();
            foreach (var p in src.FormParams)
                FormParams.Add(new HeaderItemViewModel(p => FormParams.Remove(p)) { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled });

            FormParts.Clear();
            foreach (var p in src.FormParts)
                FormParts.Add(new FormPartViewModel(p => FormParts.Remove(p)) { Key = p.Key, Value = p.Value, PartContentType = p.PartContentType, IsEnabled = p.IsEnabled });

            SetCustomBodyContentType(src._customBodyContentType);
            SelectedBodyContentType = src.SelectedBodyContentType;
        }
        finally
        {
            _syncingUrl = false;
            _syncingContentType = false;
            _syncingRaw = false;
        }

        RebuildRaw();
    }

    public void MarkAsSaved(Guid requestId, Guid collectionId, Guid? folderId)
    {
        SavedRequestId = requestId;
        SavedCollectionId = collectionId;
        SavedFolderId = folderId;
        OnPropertyChanged(nameof(TabTitle));
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
        QueryParams = [],
        FormParams = FormParams
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => new HeaderItem { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled })
            .ToList(),
        FormParts = FormParts
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => new FormPart { Key = p.Key, Value = p.Value, ContentType = p.PartContentType, IsEnabled = p.IsEnabled })
            .ToList()
    };

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
                .ToList(),
            FormParams = FormParams
                .Select(p => new HeaderItem { Key = p.Key, Value = p.Value, IsEnabled = p.IsEnabled })
                .ToList(),
            FormParts = FormParts
                .Select(p => new FormPart { Key = p.Key, Value = p.Value, ContentType = p.PartContentType, IsEnabled = p.IsEnabled })
                .ToList()
        };
    }

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

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
        finally { IsSending = false; }
    }

    [RelayCommand] private void CancelRequest() => _cts?.Cancel();
    [RelayCommand] private void AddHeader() => Headers.Add(new HeaderItemViewModel(h => Headers.Remove(h)));
    [RelayCommand] private void AddQueryParam() => QueryParams.Add(new HeaderItemViewModel(p => QueryParams.Remove(p)));
    [RelayCommand] private void AddFormParam() => FormParams.Add(new HeaderItemViewModel(p => FormParams.Remove(p)));
    [RelayCommand] private void AddFormPart() => FormParts.Add(new FormPartViewModel(p => FormParts.Remove(p)));

    [RelayCommand]
    private void ConfirmBodyClear()
    {
        IsBodyClearConfirmDialogOpen = false;
        BodyContent = string.Empty;
    }

    [RelayCommand]
    private void CancelBodyClear()
    {
        IsBodyClearConfirmDialogOpen = false;
        _revertingBodyType = true;
        try { SelectedBodyContentType = _previousBodyContentType; }
        finally { _revertingBodyType = false; }
    }

    [RelayCommand] private void Close() { _isClosing = true; IsBodyClearConfirmDialogOpen = false; _closeAction(this); }
    [RelayCommand] private void Activate() => _activateAction?.Invoke(this);
    [RelayCommand] private void OpenSaveDialog() => _saveDialogAction?.Invoke(this);
    [RelayCommand] private void Duplicate() => _duplicateAction?.Invoke(this);

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

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
        catch { return text; }
    }
}
