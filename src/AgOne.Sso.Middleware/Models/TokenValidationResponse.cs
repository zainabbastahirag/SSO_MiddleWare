using System.Text.Json.Serialization;

namespace AgOne.Sso.Models;

/// <summary>
/// Response model from AG ONE's token validation/refresh endpoint.
/// </summary>
public class TokenValidationResponse
{
    /// <summary>
    /// The valid (possibly refreshed) access token.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    /// Whether the token validation was successful.
    /// </summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    /// <summary>
    /// Error or status message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
