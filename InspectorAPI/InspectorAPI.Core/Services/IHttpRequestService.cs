using InspectorAPI.Core.Models;

namespace InspectorAPI.Core.Services;

public interface IHttpRequestService
{
    Task<HttpResponseModel> SendAsync(HttpRequestModel request, CancellationToken cancellationToken = default);
}
