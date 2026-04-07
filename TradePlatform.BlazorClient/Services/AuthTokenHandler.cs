using System.Net.Http.Headers;

namespace TradePlatform.BlazorClient.Services;

public sealed class AuthTokenHandler(IAuthTokenAccessor authTokenAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = authTokenAccessor.CurrentToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
