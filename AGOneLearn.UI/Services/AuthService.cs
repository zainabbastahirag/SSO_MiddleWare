using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AGOneLearn.UI.Models;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace AGOneLearn.UI.Services;

/// <summary>
/// AuthService retrieves user info directly from the stored token / local storage,
/// deliberately avoiding any dependency on AuthenticationStateProvider to prevent
/// a circular dependency chain.
/// </summary>
public class AuthService : IAuthService
{
    private const string TokenKey = "authToken";

    private readonly ILocalStorageService _localStorage;
    private readonly NavigationManager _navigationManager;
    private readonly IServiceProvider _serviceProvider;

    public AuthService(
        ILocalStorageService localStorage,
        NavigationManager navigationManager,
        IServiceProvider serviceProvider)
    {
        _localStorage = localStorage;
        _navigationManager = navigationManager;
        _serviceProvider = serviceProvider;
    }

    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        try
        {
            var token = await _localStorage.GetItemAsStringAsync(TokenKey);

            if (string.IsNullOrWhiteSpace(token))
                return null;

            token = token.Trim('"');

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
                return null;

            var jwt = handler.ReadJwtToken(token);

            if (jwt.ValidTo < DateTime.UtcNow)
            {
                await _localStorage.RemoveItemAsync(TokenKey);
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
        await _localStorage.RemoveItemAsync(TokenKey);
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
}
