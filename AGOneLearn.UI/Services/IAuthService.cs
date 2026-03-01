using AGOneLearn.UI.Models;

namespace AGOneLearn.UI.Services;

public interface IAuthService
{
    Task<UserInfo?> GetCurrentUserAsync();
    Task LoginAsync(string returnUrl = "/");
    Task LogoutAsync();
    void NotifyAuthenticationStateChanged();
}
