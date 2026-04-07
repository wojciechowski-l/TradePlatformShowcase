namespace TradePlatform.BlazorClient.Services;

public sealed class AuthState
{
    public string? Token { get; private set; }
    public string UserEmail { get; private set; } = string.Empty;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);

    public event Action? Changed;

    public void Login(string token, string email)
    {
        Token = token;
        UserEmail = email;
        Changed?.Invoke();
    }

    public void Logout()
    {
        Token = null;
        UserEmail = string.Empty;
        Changed?.Invoke();
    }
}
