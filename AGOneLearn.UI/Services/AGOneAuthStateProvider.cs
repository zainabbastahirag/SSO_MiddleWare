using System.Security.Claims;
using AGOneLearn.UI.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace AGOneLearn.UI.Services;

public class AGOneAuthStateProvider : AuthenticationStateProvider
{
    private readonly IAuthService _authService;

    public AGOneAuthStateProvider(IAuthService authService)
    {
        _authService = authService;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = await _authService.GetCurrentUserAsync();

        if (user == null)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName)
        };

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "AGOneAuth");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
