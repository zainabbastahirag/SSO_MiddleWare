using System.Net.Http.Json;
using System.Security.Claims;
using AgOne.Sso.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace AgOne.Sso.Blazor;

/// <summary>
/// Blazor WASM AuthenticationStateProvider that checks auth status via the backend API.
/// 
/// The backend middleware handles all token storage in HttpOnly cookies (not accessible from JS).
/// This provider calls a lightweight API endpoint to get the current user's claims.
/// 
/// Register in your Blazor WASM Program.cs:
/// <code>
/// builder.Services.AddScoped&lt;AuthenticationStateProvider, AgOneSsoAuthStateProvider&gt;();
/// </code>
/// </summary>
public class AgOneSsoAuthStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _authInfoEndpoint;

    /// <summary>
    /// Creates a new instance of the auth state provider.
    /// </summary>
    /// <param name="httpClient">HttpClient configured with the backend base address.</param>
    /// <param name="authInfoEndpoint">
    /// The relative URL of the auth info API endpoint. Defaults to "api/auth/sso-user-info".
    /// </param>
    public AgOneSsoAuthStateProvider(HttpClient httpClient, string? authInfoEndpoint = null)
    {
        _httpClient = httpClient;
        _authInfoEndpoint = authInfoEndpoint ?? "api/auth/sso-user-info";
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(_authInfoEndpoint);

            if (!response.IsSuccessStatusCode)
            {
                return CreateAnonymousState();
            }

            var userInfo = await response.Content.ReadFromJsonAsync<SsoUserInfo>();

            if (userInfo == null || !userInfo.IsAuthenticated)
            {
                return CreateAnonymousState();
            }

            var claims = new List<Claim>();

            // Standard identity claims
            if (!string.IsNullOrEmpty(userInfo.UserId))
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userInfo.UserId));

            if (!string.IsNullOrEmpty(userInfo.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, userInfo.Email));
                claims.Add(new Claim(ClaimTypes.Name, userInfo.Email));
            }

            if (!string.IsNullOrEmpty(userInfo.Name))
                claims.Add(new Claim("name", userInfo.Name));

            // Roles
            foreach (var role in userInfo.Roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            // Additional claims from the JWT
            foreach (var (key, value) in userInfo.Claims)
            {
                // Avoid duplicating claims we already added
                if (key != ClaimTypes.NameIdentifier && key != ClaimTypes.Email &&
                    key != ClaimTypes.Name && key != ClaimTypes.Role && key != "name")
                {
                    claims.Add(new Claim(key, value));
                }
            }

            var identity = new ClaimsIdentity(claims, "AgOneSso");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return CreateAnonymousState();
        }
    }

    /// <summary>
    /// Call this to force the Blazor app to re-check authentication state.
    /// Useful after login or when you suspect the token has changed.
    /// </summary>
    public void NotifyAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static AuthenticationState CreateAnonymousState()
    {
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }
}
