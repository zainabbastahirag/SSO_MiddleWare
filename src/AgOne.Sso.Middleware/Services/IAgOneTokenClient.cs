namespace AgOne.Sso.Services;

/// <summary>
/// HTTP client interface for communicating with AG ONE's token validation/refresh API.
/// </summary>
public interface IAgOneTokenClient
{
    /// <summary>
    /// Sends the current (possibly expired) access token to AG ONE's validation endpoint.
    /// If the token is expired, AG ONE will attempt to refresh it using the stored refresh token.
    /// Returns the valid access token, or null if validation/refresh failed.
    /// </summary>
    /// <param name="currentToken">The current access token (may be expired).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A valid access token, or null if the token could not be refreshed.</returns>
    Task<string?> ValidateAndRefreshTokenAsync(string currentToken, CancellationToken cancellationToken = default);
}
