using Microsoft.AspNetCore.Http;

namespace AgOne.Sso.Models;

/// <summary>
/// Configuration options for the AG ONE SSO middleware.
/// Add these values to your appsettings.json under the "AgOneSso" section.
/// </summary>
public class AgOneSsoOptions
{
    /// <summary>
    /// The configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "AgOneSso";

    // ───────────────────────── AG ONE Central Gateway ─────────────────────────

    /// <summary>
    /// Base URL of the AG ONE backend API (e.g., "https://agone.example.com").
    /// Used to call the token refresh/validate endpoint.
    /// </summary>
    public string AgOneBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The full login page URL on AG ONE where unauthenticated users are redirected.
    /// Defaults to AgOneBaseUrl root if not explicitly set.
    /// Example: "https://agone.example.com/login"
    /// </summary>
    public string AgOneLoginUrl { get; set; } = string.Empty;

    /// <summary>
    /// The relative path on AG ONE's API for token validation/refresh.
    /// Defaults to "api/auth/external/validate".
    /// </summary>
    public string TokenValidateEndpoint { get; set; } = "api/auth/external/validate";

    // ───────────────────────── JWT / Entra ID Validation ─────────────────────────

    /// <summary>
    /// The Entra ID (Azure AD) authority URL for JWT validation.
    /// Example: "https://login.microsoftonline.com/{tenant-id}/v2.0"
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// The expected audience claim in the JWT (usually the Entra ID Client ID or API scope).
    /// </summary>
    public string ValidAudience { get; set; } = string.Empty;

    /// <summary>
    /// Additional valid audiences (optional). Useful when tokens may target multiple APIs.
    /// </summary>
    public List<string> AdditionalAudiences { get; set; } = new();

    /// <summary>
    /// The Entra ID Tenant ID. Used to construct the OIDC metadata endpoint if Authority is not set.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The Entra ID Client ID. Used as a fallback audience if ValidAudience is not set.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    // ───────────────────────── Cookie Settings ─────────────────────────

    /// <summary>
    /// Name of the long-lived session cookie that stores the SSO token.
    /// </summary>
    public string SessionCookieName { get; set; } = "agone_sso_token";

    /// <summary>
    /// Name of the short-lived launch cookie set by AG ONE's Product Launcher.
    /// </summary>
    public string LaunchCookieName { get; set; } = "agone_launch_token";

    /// <summary>
    /// Session cookie lifetime. Defaults to 60 minutes.
    /// The middleware refreshes the token before it expires, so this is the max idle time.
    /// </summary>
    public TimeSpan SessionCookieLifetime { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Cookie domain. Leave empty to use the current request domain.
    /// Set this if products share a common parent domain (e.g., ".example.com").
    /// </summary>
    public string? CookieDomain { get; set; }

    /// <summary>
    /// SameSite mode for cookies. Defaults to None for cross-site SSO flows.
    /// </summary>
    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.None;

    // ───────────────────────── Behavior Settings ─────────────────────────

    /// <summary>
    /// Number of minutes before token expiry to proactively refresh.
    /// Defaults to 5 minutes.
    /// </summary>
    public int RefreshBufferMinutes { get; set; } = 5;

    /// <summary>
    /// Paths that should skip authentication entirely (e.g., health checks, static files).
    /// Supports simple prefix matching. Always includes "/_framework", "/_content", "/favicon.ico".
    /// </summary>
    public List<string> AnonymousPaths { get; set; } = new();

    /// <summary>
    /// If true, this instance IS the AG ONE central gateway.
    /// When set, the middleware won't call AG ONE's API for refresh (it's already on AG ONE).
    /// Token refresh is handled by AG ONE's own auth flow.
    /// </summary>
    public bool IsAgOneGateway { get; set; } = false;

    /// <summary>
    /// When true, tokens received via query string parameter "token" will also be accepted.
    /// This supports the Product Launcher redirect flow. Defaults to true.
    /// </summary>
    public bool AcceptTokenFromQueryString { get; set; } = true;

    /// <summary>
    /// Query string parameter name for the token. Defaults to "token".
    /// </summary>
    public string TokenQueryParameterName { get; set; } = "token";

    // ───────────────────────── Computed Properties ─────────────────────────

    /// <summary>
    /// Resolves the effective login URL.
    /// </summary>
    internal string EffectiveLoginUrl =>
        !string.IsNullOrEmpty(AgOneLoginUrl)
            ? AgOneLoginUrl
            : AgOneBaseUrl.TrimEnd('/');

    /// <summary>
    /// Resolves the full token validation endpoint URL.
    /// </summary>
    internal string TokenValidateUrl =>
        $"{AgOneBaseUrl.TrimEnd('/')}/{TokenValidateEndpoint.TrimStart('/')}";

    /// <summary>
    /// Resolves the effective authority for OIDC discovery.
    /// </summary>
    internal string EffectiveAuthority =>
        !string.IsNullOrEmpty(Authority)
            ? Authority
            : $"https://login.microsoftonline.com/{TenantId}/v2.0";

    /// <summary>
    /// Resolves all valid audiences for JWT validation.
    /// </summary>
    internal IEnumerable<string> AllValidAudiences
    {
        get
        {
            var audiences = new List<string>();
            if (!string.IsNullOrEmpty(ValidAudience)) audiences.Add(ValidAudience);
            if (!string.IsNullOrEmpty(ClientId)) audiences.Add(ClientId);
            // Entra ID tokens often use api://{clientId} as audience
            if (!string.IsNullOrEmpty(ClientId)) audiences.Add($"api://{ClientId}");
            audiences.AddRange(AdditionalAudiences);
            return audiences.Distinct();
        }
    }

    /// <summary>
    /// Default paths that should always be anonymous.
    /// </summary>
    internal static readonly string[] DefaultAnonymousPrefixes = new[]
    {
        "/_framework",
        "/_content",
        "/_blazor",
        "/favicon.ico",
        "/css",
        "/js",
        "/images",
        "/fonts",
        "/_vs"
    };
}
