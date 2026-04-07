using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using TradePlatform.Core.Entities;

namespace TradePlatform.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapTradeAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth");

        group.MapPost("/login", LoginAsync);
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/logout", LogoutAsync);

        return endpoints;
    }

    private static async Task<RedirectHttpResult> LoginAsync(
        [FromForm] LoginForm form,
        SignInManager<ApplicationUser> signInManager)
    {
        var result = await signInManager.PasswordSignInAsync(
            form.Email,
            form.Password,
            isPersistent: false,
            lockoutOnFailure: false);

        if (result.Succeeded)
        {
            return TypedResults.Redirect(NormalizeReturnUrl(form.ReturnUrl));
        }

        return TypedResults.Redirect(BuildRedirectUri("/", new Dictionary<string, string?>
        {
            ["mode"] = "login",
            ["authError"] = "Invalid email or password."
        }));
    }

    private static async Task<RedirectHttpResult> RegisterAsync(
        [FromForm] RegisterForm form,
        UserManager<ApplicationUser> userManager)
    {
        var user = new ApplicationUser
        {
            UserName = form.Email,
            Email = form.Email,
            FullName = form.Email
        };

        var result = await userManager.CreateAsync(user, form.Password);
        if (result.Succeeded)
        {
            return TypedResults.Redirect(BuildRedirectUri("/", new Dictionary<string, string?>
            {
                ["mode"] = "login",
                ["authSuccess"] = "Registration Successful! Please log in."
            }));
        }

        var errorMessage = result.Errors.FirstOrDefault()?.Description ?? "Registration failed.";

        return TypedResults.Redirect(BuildRedirectUri("/", new Dictionary<string, string?>
        {
            ["mode"] = "register",
            ["authError"] = errorMessage
        }));
    }

    private static async Task<RedirectHttpResult> LogoutAsync(
        [FromForm] LogoutForm form,
        SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.Redirect(NormalizeReturnUrl(form.ReturnUrl));
    }

    private static string BuildRedirectUri(string path, IReadOnlyDictionary<string, string?> queryParameters) =>
        QueryHelpers.AddQueryString(path, queryParameters.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)));

    private static string NormalizeReturnUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? "/"
            : returnUrl;

    public sealed class LoginForm
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ReturnUrl { get; set; }
    }

    public sealed class RegisterForm
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ReturnUrl { get; set; }
    }

    public sealed class LogoutForm
    {
        public string? ReturnUrl { get; set; }
    }
}
