using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using InspectorAPI.Core.Models;

namespace InspectorAPI.Core.Services;

public class HttpRequestService : IHttpRequestService
{
    private readonly HttpClient _httpClient;

    public HttpRequestService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<HttpResponseModel> SendAsync(HttpRequestModel request, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = BuildUrl(request.Url, request.QueryParams);
            using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), url);

            // Add headers
            foreach (var header in request.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
            {
                if (!httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    httpRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add body for methods that support it
            if (!string.IsNullOrEmpty(request.Body) && request.Method is not "GET" and not "HEAD" and not "DELETE")
            {
                var content = new StringContent(request.Body, Encoding.UTF8);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.BodyContentType);
                httpRequest.Content = content;
            }

            var sw = Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            sw.Stop();

            var bodyText = Encoding.UTF8.GetString(bodyBytes);

            var headers = new Dictionary<string, string>();
            foreach (var h in response.Headers)
                headers[h.Key] = string.Join(", ", h.Value);
            foreach (var h in response.Content.Headers)
                headers[h.Key] = string.Join(", ", h.Value);

            return new HttpResponseModel
            {
                StatusCode = (int)response.StatusCode,
                StatusText = response.ReasonPhrase ?? response.StatusCode.ToString(),
                Headers = headers,
                Body = bodyText,
                ElapsedMilliseconds = sw.ElapsedMilliseconds,
                BodySizeBytes = bodyBytes.Length
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new HttpResponseModel { ErrorMessage = "Request timed out." };
        }
        catch (Exception ex)
        {
            return new HttpResponseModel { ErrorMessage = ex.Message };
        }
    }

    private static string BuildUrl(string baseUrl, IEnumerable<HeaderItem> queryParams)
    {
        var enabledParams = queryParams.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
        if (enabledParams.Count == 0) return baseUrl;

        var query = string.Join("&", enabledParams.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return baseUrl.Contains('?') ? $"{baseUrl}&{query}" : $"{baseUrl}?{query}";
    }
}
