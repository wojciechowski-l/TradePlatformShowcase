using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TradePlatform.BlazorClient.Models;

namespace TradePlatform.BlazorClient.Services;

public sealed class ApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/auth/login?useCookies=false", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, "Login failed"));
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Login succeeded but no token payload was returned.");
    }

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/auth/register", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, "Registration failed"));
        }
    }

    public async Task<AccountDto?> GetMyAccountAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/accounts/my-account", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, "Failed to fetch account."));
        }

        return await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions, cancellationToken);
    }

    public async Task<AccountDto> ProvisionAccountAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/accounts/provision", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, "Failed to provision account."));
        }

        return await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Provisioning succeeded but no account was returned.");
    }

    public async Task<IReadOnlyList<AccountActivityDto>> GetMyAccountActivityAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/accounts/my-account/activity", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, "Failed to fetch the activity feed."));
        }

        return await response.Content.ReadFromJsonAsync<List<AccountActivityDto>>(JsonOptions, cancellationToken) ?? [];
    }

    public async Task<TransactionResponse> SubmitTransactionAsync(
        TransactionRequest request,
        string token,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var message = CreateAuthorizedRequest(HttpMethod.Post, "api/transactions", token);
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        message.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Unauthorized: session expired.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Transaction already processing. Please wait.");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>(JsonOptions, cancellationToken);
            throw new ApiValidationException(problem?.Errors ?? []);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, "Transaction submission failed."));
        }

        return await response.Content.ReadFromJsonAsync<TransactionResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Transaction submission succeeded but no payload was returned.");
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, string fallbackMessage)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return fallbackMessage;
        }

        try
        {
            var problem = JsonSerializer.Deserialize<ProblemDetailsEnvelope>(content, JsonOptions);
            if (problem is not null)
            {
                if (!string.IsNullOrWhiteSpace(problem.Detail))
                {
                    return problem.Detail;
                }

                if (!string.IsNullOrWhiteSpace(problem.Title))
                {
                    return problem.Title;
                }
            }
            
            var messageEnvelope = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content, JsonOptions);
            if (messageEnvelope is not null && messageEnvelope.TryGetValue("message", out var messageElement))
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch
        {
        }

        return content;
    }
}

public sealed class ApiValidationException(Dictionary<string, string[]> errors) : Exception("Validation failed.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

file sealed class ProblemDetailsEnvelope
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
}
