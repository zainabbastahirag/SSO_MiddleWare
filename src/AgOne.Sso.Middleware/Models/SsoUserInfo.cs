using System.Text.Json.Serialization;

namespace AgOne.Sso.Models;

/// <summary>
/// Lightweight user info model returned by the auth state endpoint.
/// Used by Blazor WASM to check authentication status and display user info.
/// </summary>
public class SsoUserInfo
{
    [JsonPropertyName("isAuthenticated")]
    public bool IsAuthenticated { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("claims")]
    public Dictionary<string, string> Claims { get; set; } = new();
}
