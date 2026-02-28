// ═══════════════════════════════════════════════════════════════════════════════
// AG ONE ONLY — Implement ILocalTokenRefreshService
// ═══════════════════════════════════════════════════════════════════════════════
//
// Copy this file into your AG ONE Server project.
// It uses your existing DB (UserTokens) + Entra ID to refresh tokens locally.
//
// Register in AG ONE's Program.cs:
//   builder.Services.AddScoped<ILocalTokenRefreshService, AgOneLocalTokenRefreshService>();
//
// ═══════════════════════════════════════════════════════════════════════════════

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgOne.Sso;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AGOne.API.Services;

public class AgOneLocalTokenRefreshService : ILocalTokenRefreshService
{
    // ── Inject YOUR DbContext and config ──
    // Replace "YourDbContext" with your actual DbContext class name
    private readonly YourDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgOneLocalTokenRefreshService> _logger;

    public AgOneLocalTokenRefreshService(
        YourDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<AgOneLocalTokenRefreshService> logger)
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> RefreshTokenAsync(string? userId, string? tenantId, string? currentToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("RefreshTokenAsync called with no userId");
            return null;
        }

        // ── 1. Find the user's token in DB ──
        var userToken = await _db.UserTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IsActive, ct);

        if (userToken == null)
        {
            _logger.LogWarning("No active token found for userId={UserId}", userId);
            return null;
        }

        // ── 2. If Entra ID access token is still valid, generate a new custom JWT ──
        if (userToken.AccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogDebug("Entra token still valid for userId={UserId}, generating new custom JWT", userId);
            return GenerateCustomJwt(userToken);
        }

        // ── 3. Entra ID token expired — refresh it ──
        if (string.IsNullOrEmpty(userToken.RefreshToken))
        {
            _logger.LogWarning("No refresh token for userId={UserId}", userId);
            return null;
        }

        var instance = _config["AzureAd:Instance"]?.TrimEnd('/') ?? "https://login.microsoftonline.com";
        var entraTenanId = _config["AzureAd:TenantId"] ?? "";
        var clientId = _config["AzureAd:ClientId"] ?? "";
        var clientSecret = _config["AzureAd:ClientSecret"] ?? "";
        var endpoint = $"{instance}/{entraTenanId}/oauth2/v2.0/token";

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = userToken.RefreshToken
        };
        if (!string.IsNullOrEmpty(userToken.GrantedScopes))
            form["scope"] = userToken.GrantedScopes;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.PostAsync(endpoint, new FormUrlEncodedContent(form), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Entra ID refresh failed for userId={UserId}: {Body}", userId, body);
                return null;
            }

            var tr = JsonSerializer.Deserialize<EntraTokenResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (tr == null || string.IsNullOrEmpty(tr.AccessToken))
            {
                _logger.LogError("Entra ID refresh returned no access token for userId={UserId}", userId);
                return null;
            }

            // ── 4. Update DB with new tokens ──
            userToken.AccessToken = tr.AccessToken;
            userToken.RefreshToken = tr.RefreshToken ?? userToken.RefreshToken;
            userToken.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(tr.ExpiresIn);
            userToken.UpdatedUtc = DateTime.UtcNow;
            userToken.RefreshCount++;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Entra ID token refreshed for userId={UserId}", userId);

            // ── 5. Generate a new custom JWT with updated info ──
            return GenerateCustomJwt(userToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Entra ID token for userId={UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Generates the custom JWT (HS256) that AG ONE uses for its Blazor frontend.
    /// This is the same token format that gets stored in LocalStorage and sent to products.
    /// ── ADAPT THIS to match your existing JWT generation logic ──
    /// </summary>
    private string GenerateCustomJwt(dynamic userToken)
    {
        var secret = _config["Jwt:Secret"] ?? "AGOneSecretKey123456789012345678901234567890";
        var issuer = _config["Jwt:Issuer"] ?? "https://yourmarketplace.com";
        var audience = _config["Jwt:Audience"] ?? "https://yourmarketplace.com";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", userToken.UserId?.ToString() ?? ""),
            new("tenant_id", userToken.TenantId?.ToString() ?? ""),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// ── Entra ID token response ──
// You may already have this class. If so, use yours instead.
public class EntraTokenResponse
{
    [JsonPropertyName("access_token")]  public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")]    public int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")]    public string? TokenType { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
//
// SETUP IN AG ONE's Program.cs — add this ONE line:
//
//   builder.Services.AddScoped<ILocalTokenRefreshService, AgOneLocalTokenRefreshService>();
//
// That's it. The middleware will automatically call this when tokens expire.
//
// ═══════════════════════════════════════════════════════════════════════════════
