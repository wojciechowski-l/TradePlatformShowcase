using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace TradePlatform.BlazorClient.Services;

public interface IAuthTokenAccessor
{
    string? CurrentToken { get; }
}

public sealed class ClientAuthenticationStateProvider : AuthenticationStateProvider, IAuthTokenAccessor
{
    private static readonly AuthenticationState AnonymousState = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private string? _currentToken;
    private string? _userEmail;

    public string? CurrentToken => _currentToken;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentToken))
        {
            return Task.FromResult(AnonymousState);
        }

        var identity = CreateIdentity(_currentToken, _userEmail);
        if (identity is null)
        {
            _currentToken = null;
            _userEmail = null;
            return Task.FromResult(AnonymousState);
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public void SetAuthentication(string accessToken, string? userEmail = null)
    {
        _currentToken = accessToken;
        _userEmail = userEmail;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void ClearAuthentication()
    {
        _currentToken = null;
        _userEmail = null;
        NotifyAuthenticationStateChanged(Task.FromResult(AnonymousState));
    }

    private static ClaimsIdentity? CreateIdentity(string accessToken, string? userEmail)
    {
        try
        {
            var claims = ParseClaims(accessToken);
            var expiration = claims.FirstOrDefault(claim => claim.Type == "exp")?.Value;

            if (long.TryParse(expiration, out var exp)
                && DateTimeOffset.FromUnixTimeSeconds(exp) <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                EnsureClaim(claims, ClaimTypes.Email, userEmail);
                EnsureClaim(claims, "email", userEmail);
                EnsureClaim(claims, ClaimTypes.Name, userEmail);
            }

            return new ClaimsIdentity(claims, authenticationType: "Bearer", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
        }
        catch
        {
            return null;
        }
    }

    private static List<Claim> ParseClaims(string jwt)
    {
        var segments = jwt.Split('.');
        if (segments.Length < 2)
        {
            return [];
        }

        var jsonBytes = ParseBase64WithoutPadding(segments[1]);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes) ?? [];
        var claims = new List<Claim>();

        foreach (var (type, value) in payload)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var arrayItem in value.EnumerateArray())
                {
                    claims.Add(new Claim(type, arrayItem.ToString()));
                }

                continue;
            }

            claims.Add(new Claim(type, value.ToString()));
        }

        MapClaim(claims, "email", ClaimTypes.Email);
        MapClaim(claims, "name", ClaimTypes.Name);
        MapClaim(claims, "unique_name", ClaimTypes.Name);
        MapClaim(claims, "sub", ClaimTypes.NameIdentifier);
        MapClaim(claims, "role", ClaimTypes.Role);

        return claims;
    }

    private static void MapClaim(List<Claim> claims, string sourceType, string targetType)
    {
        var sourceClaims = claims.Where(claim => claim.Type == sourceType).ToList();
        foreach (var claim in sourceClaims)
        {
            if (!claims.Any(existing => existing.Type == targetType && existing.Value == claim.Value))
            {
                claims.Add(new Claim(targetType, claim.Value));
            }
        }
    }

    private static void EnsureClaim(List<Claim> claims, string type, string value)
    {
        if (!claims.Any(claim => claim.Type == type && claim.Value == value))
        {
            claims.Add(new Claim(type, value));
        }
    }

    private static byte[] ParseBase64WithoutPadding(string base64)
    {
        base64 = base64.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (base64.Length % 4);
        if (padding is > 0 and < 4)
        {
            base64 = base64.PadRight(base64.Length + padding, '=');
        }

        return Convert.FromBase64String(base64);
    }
}
