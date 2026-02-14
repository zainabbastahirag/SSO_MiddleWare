using System.Text.Json.Serialization;

namespace AgOne.Sso.Models;

/// <summary>
/// Request model for calling AG ONE's token validation/refresh endpoint.
/// Matches the AG ONE controller's ExternalTokenValidationRequest.
/// </summary>
public class ExternalTokenValidationRequest
{
    /// <summary>
    /// The current access token (may be expired).
    /// AG ONE will look up the user by this token and refresh if needed.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    /// The ID token. AG ONE can also look up the user by ID token.
    /// </summary>
    [JsonPropertyName("idToken")]
    public string? IdToken { get; set; }
}
