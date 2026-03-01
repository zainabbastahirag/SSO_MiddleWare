using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AGOneLearn.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AGOneLearn.UI.Services;

/// <summary>
/// AuthService retrieves user info directly from the JWT stored in a cookie,
/// deliberately avoiding any dependency on AuthenticationStateProvider to prevent
/// a circular dependency chain.
/// </summary>
public class AuthService : IAuthService
{
    private const string TokenCookieName = "authToken";

    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigationManager;
    private readonly IServiceProvider _serviceProvider;

    public AuthService(
        IJSRuntime jsRuntime,
        NavigationManager navigationManager,
        IServiceProvider serviceProvider)
    {
        _jsRuntime = jsRuntime;
        _navigationManager = navigationManager;
        _serviceProvider = serviceProvider;
    }

    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        try
        {
            var token = await GetCookieValueAsync(TokenCookieName);

            if (string.IsNullOrWhiteSpace(token))
                return null;

            token = token.Trim('"');

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
                return null;

            var jwt = handler.ReadJwtToken(token);

            if (jwt.ValidTo < DateTime.UtcNow)
            {
                await DeleteCookieAsync(TokenCookieName);
                return null;
            }

            var claims = jwt.Claims.ToList();

            var roles = claims
                .Where(c => c.Type == ClaimTypes.Role
                          || c.Type == "role"
                          || c.Type == "roles"
                          || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var email = claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Email || c.Type == "email")?.Value
                ?? string.Empty;

            var displayName = claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Name || c.Type == "name" || c.Type == "preferred_username")?.Value
                ?? email;

            var subClaim = claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.NameIdentifier || c.Type == "sub")?.Value;

            return new UserInfo
            {
                Id = Guid.TryParse(subClaim, out var id) ? id : Guid.NewGuid(),
                Email = email,
                DisplayName = displayName,
                Roles = roles
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task LoginAsync(string returnUrl = "/")
    {
        _navigationManager.NavigateTo($"authentication/login?returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
        await Task.CompletedTask;
    }

    public async Task LogoutAsync()
    {
        await DeleteCookieAsync(TokenCookieName);
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

    private async Task<string?> GetCookieValueAsync(string cookieName)
    {
        var allCookies = await _jsRuntime.InvokeAsync<string>("eval", "document.cookie");

        if (string.IsNullOrEmpty(allCookies))
            return null;

        var cookies = allCookies.Split(';', StringSplitOptions.TrimEntries);
        foreach (var cookie in cookies)
        {
            var parts = cookie.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim() == cookieName)
                return Uri.UnescapeDataString(parts[1].Trim());
        }

        return null;
    }

    private async Task DeleteCookieAsync(string cookieName)
    {
        await _jsRuntime.InvokeVoidAsync("eval",
            $"document.cookie = '{cookieName}=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;'");
    }
}
