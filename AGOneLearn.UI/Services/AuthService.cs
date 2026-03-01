using System.Net.Http.Json;
using System.Security.Claims;
using AGOneLearn.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace AGOneLearn.UI.Services;

/// <summary>
/// Retrieves user info by calling the server's /api/auth/sso-user-info endpoint.
/// The HttpOnly SSO cookie is sent automatically by the browser (via CookieHandler).
/// No dependency on AuthenticationStateProvider — breaks the circular dependency.
/// </summary>
public class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly NavigationManager _navigationManager;
    private readonly IServiceProvider _serviceProvider;

    public AuthService(
        HttpClient http,
        NavigationManager navigationManager,
        IServiceProvider serviceProvider)
    {
        _http = http;
        _navigationManager = navigationManager;
        _serviceProvider = serviceProvider;
    }

    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/sso-user-info");

            if (!response.IsSuccessStatusCode)
                return null;

            var ssoUser = await response.Content.ReadFromJsonAsync<SsoUserInfoResponse>();

            if (ssoUser == null || !ssoUser.IsAuthenticated)
                return null;

            return new UserInfo
            {
                Id = Guid.TryParse(ssoUser.UserId, out var id) ? id : Guid.NewGuid(),
                Email = ssoUser.Email ?? string.Empty,
                DisplayName = ssoUser.Name ?? ssoUser.Email ?? string.Empty,
                Roles = ssoUser.Roles ?? new List<string>()
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task LoginAsync(string returnUrl = "/")
    {
        var agOneLoginUrl = _navigationManager.BaseUri.TrimEnd('/');
        _navigationManager.NavigateTo(
            $"{agOneLoginUrl}/authentication/login?returnUrl={Uri.EscapeDataString(returnUrl)}",
            forceLoad: true);
        await Task.CompletedTask;
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _http.PostAsync("api/auth/sso-logout", null);
        }
        catch { }

        NotifyAuthenticationStateChanged();
        _navigationManager.NavigateTo("/", forceLoad: true);
    }

    public void NotifyAuthenticationStateChanged()
    {
        var authStateProvider = _serviceProvider.GetService<AuthenticationStateProvider>();
        if (authStateProvider is AGOneAuthStateProvider agProvider)
        {
            agProvider.NotifyStateChanged();
        }
    }

    private class SsoUserInfoResponse
    {
        public bool IsAuthenticated { get; set; }
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public List<string> Roles { get; set; } = new();
        public Dictionary<string, string> Claims { get; set; } = new();
    }
}
