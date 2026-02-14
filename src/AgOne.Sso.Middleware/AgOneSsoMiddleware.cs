using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AgOne.Sso.Models;
using AgOne.Sso.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AgOne.Sso;

/// <summary>
/// ASP.NET Core middleware that handles SSO authentication for all AG ONE products.
/// 
/// Flow:
/// 1. Checks if the request path is anonymous (static files, health checks, etc.) → skip.
/// 2. Extracts token from (in priority order):
///    a. Authorization header (Bearer token) — for API calls from Blazor WASM
///    b. Session cookie — for subsequent page requests
///    c. Launch cookie — for the first request after Product Launcher redirect
///    d. Query string — for the initial redirect from AG ONE
/// 3. Validates the JWT locally using Entra ID's signing keys (fast, no network call).
/// 4. If valid and not expiring soon → sets ClaimsPrincipal, continues.
/// 5. If expired or expiring within the buffer window → calls AG ONE to refresh.
/// 6. If refresh succeeds → updates cookie, sets ClaimsPrincipal, continues.
/// 7. If no token or all validation fails:
///    - API request (XHR/fetch) → returns 401 JSON response
///    - Page request → redirects to AG ONE login
/// </summary>
public class AgOneSsoMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AgOneSsoOptions _options;
    private readonly ILogger<AgOneSsoMiddleware> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;

    public AgOneSsoMiddleware(
        RequestDelegate next,
        IOptions<AgOneSsoOptions> options,
        ILogger<AgOneSsoMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();

        // Set up OIDC configuration manager for automatic key rotation
        var authority = _options.EffectiveAuthority.TrimEnd('/');
        if (!string.IsNullOrEmpty(authority) && authority != "https://login.microsoftonline.com//v2.0")
        {
            var metadataAddress = $"{authority}/.well-known/openid-configuration";
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        // ─── Step 1: Skip anonymous paths ───
        if (IsAnonymousPath(path))
        {
            await _next(context);
            return;
        }

        // ─── Step 2: Extract token from various sources ───
        var (token, source) = ExtractToken(context);

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogDebug("No token found for path {Path}", path);
            await HandleUnauthenticated(context, "No authentication token provided");
            return;
        }

        _logger.LogDebug("Token found from {Source} for path {Path}", source, path);

        // ─── Step 3: Validate the JWT ───
        var (principal, validationResult) = await ValidateTokenAsync(token);

        string? activeToken = token;

        switch (validationResult)
        {
            case TokenValidationResult.Valid:
                // Token is valid and not expiring soon — proceed
                break;

            case TokenValidationResult.ExpiringSoon:
                // Token is valid but expiring soon — try to proactively refresh
                _logger.LogDebug("Token expiring soon, attempting proactive refresh");
                var refreshedToken = await TryRefreshTokenAsync(context, token);
                if (!string.IsNullOrEmpty(refreshedToken))
                {
                    activeToken = refreshedToken;
                    // Re-validate the new token to get updated claims
                    var (newPrincipal, newResult) = await ValidateTokenAsync(refreshedToken);
                    if (newPrincipal != null)
                    {
                        principal = newPrincipal;
                    }
                }
                // If refresh fails, the current token is still valid — continue with it
                break;

            case TokenValidationResult.Expired:
                // Token is expired — must refresh
                _logger.LogDebug("Token expired, attempting refresh via AG ONE");
                var renewed = await TryRefreshTokenAsync(context, token);
                if (!string.IsNullOrEmpty(renewed))
                {
                    activeToken = renewed;
                    var (renewedPrincipal, renewedResult) = await ValidateTokenAsync(renewed);
                    if (renewedPrincipal != null)
                    {
                        principal = renewedPrincipal;
                    }
                    else
                    {
                        _logger.LogWarning("Refreshed token failed validation");
                        await HandleUnauthenticated(context, "Token refresh returned invalid token");
                        return;
                    }
                }
                else
                {
                    _logger.LogInformation("Token expired and refresh failed");
                    await HandleUnauthenticated(context, "Token expired and could not be refreshed");
                    return;
                }
                break;

            case TokenValidationResult.Invalid:
            default:
                _logger.LogWarning("Token validation failed from source {Source}", source);
                await HandleUnauthenticated(context, "Invalid authentication token");
                return;
        }

        // ─── Step 4: Set the authenticated user ───
        if (principal != null)
        {
            context.User = principal;
        }

        // ─── Step 5: Update session cookie ───
        SetSessionCookie(context, activeToken);

        // ─── Step 6: Clean up launch cookie if that was the source ───
        if (source == TokenSource.LaunchCookie || source == TokenSource.QueryString)
        {
            // Remove the short-lived launch cookie — we've promoted it to a session cookie
            if (context.Request.Cookies.ContainsKey(_options.LaunchCookieName))
            {
                context.Response.Cookies.Delete(_options.LaunchCookieName, new CookieOptions
                {
                    Path = "/",
                    Secure = true,
                    SameSite = SameSiteMode.None
                });
            }

            // If token came from query string, redirect to clean URL (remove token from address bar)
            if (source == TokenSource.QueryString)
            {
                var cleanUrl = RemoveTokenFromQueryString(context);
                context.Response.Redirect(cleanUrl);
                return;
            }
        }

        // ─── Step 7: Continue the pipeline ───
        await _next(context);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Token Extraction
    // ═══════════════════════════════════════════════════════════════════

    private (string? Token, TokenSource Source) ExtractToken(HttpContext context)
    {
        // Priority 1: Authorization header (API calls from Blazor WASM)
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            if (!string.IsNullOrEmpty(token))
                return (token, TokenSource.AuthorizationHeader);
        }

        // Priority 2: Session cookie (subsequent requests)
        if (context.Request.Cookies.TryGetValue(_options.SessionCookieName, out var sessionToken)
            && !string.IsNullOrEmpty(sessionToken))
        {
            return (sessionToken, TokenSource.SessionCookie);
        }

        // Priority 3: Launch cookie (first request after Product Launcher redirect)
        if (context.Request.Cookies.TryGetValue(_options.LaunchCookieName, out var launchToken)
            && !string.IsNullOrEmpty(launchToken))
        {
            return (launchToken, TokenSource.LaunchCookie);
        }

        // Priority 4: Query string (Product Launcher redirect fallback)
        if (_options.AcceptTokenFromQueryString
            && context.Request.Query.TryGetValue(_options.TokenQueryParameterName, out var queryToken)
            && !string.IsNullOrEmpty(queryToken.FirstOrDefault()))
        {
            return (queryToken.FirstOrDefault()!, TokenSource.QueryString);
        }

        return (null, TokenSource.None);
    }

    // ═══════════════════════════════════════════════════════════════════
    // JWT Validation
    // ═══════════════════════════════════════════════════════════════════

    private async Task<(ClaimsPrincipal? Principal, TokenValidationResult Result)> ValidateTokenAsync(string token)
    {
        try
        {
            // If we don't have OIDC configuration, fall back to basic parsing
            if (_configManager == null)
            {
                return ValidateTokenBasic(token);
            }

            var config = await _configManager.GetConfigurationAsync(CancellationToken.None);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = true,
                ValidIssuers = new[]
                {
                    _options.EffectiveAuthority,
                    // Entra ID may issue tokens with slightly different issuer formats
                    $"https://login.microsoftonline.com/{_options.TenantId}/v2.0",
                    $"https://sts.windows.net/{_options.TenantId}/"
                },
                ValidateAudience = _options.AllValidAudiences.Any(),
                ValidAudiences = _options.AllValidAudiences,
                ValidateLifetime = false, // We handle expiry ourselves for refresh logic
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is JwtSecurityToken jwt)
            {
                return ClassifyTokenExpiry(principal, jwt);
            }

            return (principal, TokenValidationResult.Valid);
        }
        catch (SecurityTokenExpiredException)
        {
            // Token signature is valid but it's expired — we can try to refresh
            return (null, TokenValidationResult.Expired);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogDebug(ex, "JWT validation failed: {Message}", ex.Message);
            return (null, TokenValidationResult.Invalid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during JWT validation");
            return (null, TokenValidationResult.Invalid);
        }
    }

    /// <summary>
    /// Fallback validation when OIDC metadata is not available.
    /// Parses the JWT without full signature validation (useful during development
    /// or when the middleware relies entirely on AG ONE for token authority).
    /// </summary>
    private (ClaimsPrincipal? Principal, TokenValidationResult Result) ValidateTokenBasic(string token)
    {
        try
        {
            if (!_tokenHandler.CanReadToken(token))
            {
                return (null, TokenValidationResult.Invalid);
            }

            var jwt = _tokenHandler.ReadJwtToken(token);
            var identity = new ClaimsIdentity(jwt.Claims, "AgOneSso");
            var principal = new ClaimsPrincipal(identity);

            return ClassifyTokenExpiry(principal, jwt);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Basic token parsing failed");
            return (null, TokenValidationResult.Invalid);
        }
    }

    private (ClaimsPrincipal Principal, TokenValidationResult Result) ClassifyTokenExpiry(
        ClaimsPrincipal principal, JwtSecurityToken jwt)
    {
        var now = DateTime.UtcNow;
        var bufferTime = TimeSpan.FromMinutes(_options.RefreshBufferMinutes);

        if (jwt.ValidTo < now)
        {
            // Token is expired
            return (principal, TokenValidationResult.Expired);
        }

        if (jwt.ValidTo < now.Add(bufferTime))
        {
            // Token will expire within the buffer window
            return (principal, TokenValidationResult.ExpiringSoon);
        }

        return (principal, TokenValidationResult.Valid);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Token Refresh
    // ═══════════════════════════════════════════════════════════════════

    private async Task<string?> TryRefreshTokenAsync(HttpContext context, string currentToken)
    {
        // If this IS AG ONE gateway, don't call ourselves — AG ONE handles its own refresh
        if (_options.IsAgOneGateway)
        {
            _logger.LogDebug("Running on AG ONE gateway — skipping external token refresh");
            return null;
        }

        try
        {
            var tokenClient = context.RequestServices.GetService(typeof(IAgOneTokenClient)) as IAgOneTokenClient;
            if (tokenClient == null)
            {
                _logger.LogWarning("IAgOneTokenClient not registered — cannot refresh token");
                return null;
            }

            return await tokenClient.ValidateAndRefreshTokenAsync(currentToken, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Cookie Management
    // ═══════════════════════════════════════════════════════════════════

    private void SetSessionCookie(HttpContext context, string token)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = _options.CookieSameSite,
            Path = "/",
            MaxAge = _options.SessionCookieLifetime,
            IsEssential = true
        };

        if (!string.IsNullOrEmpty(_options.CookieDomain))
        {
            cookieOptions.Domain = _options.CookieDomain;
        }

        context.Response.Cookies.Append(_options.SessionCookieName, token, cookieOptions);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Unauthenticated Response Handling
    // ═══════════════════════════════════════════════════════════════════

    private async Task HandleUnauthenticated(HttpContext context, string reason)
    {
        // Clear any existing session cookie
        context.Response.Cookies.Delete(_options.SessionCookieName, new CookieOptions
        {
            Path = "/",
            Secure = true,
            SameSite = _options.CookieSameSite
        });

        // Determine if this is an API call or a page request
        if (IsApiRequest(context))
        {
            // API call — return 401 JSON
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                isAuthenticated = false,
                message = reason,
                loginUrl = _options.EffectiveLoginUrl
            });
        }
        else
        {
            // Page request — redirect to AG ONE login
            var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
            var loginUrl = $"{_options.EffectiveLoginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}";

            _logger.LogInformation("Redirecting unauthenticated request to AG ONE: {LoginUrl}", loginUrl);
            context.Response.Redirect(loginUrl);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private bool IsAnonymousPath(string path)
    {
        // Check default static file / framework paths
        foreach (var prefix in AgOneSsoOptions.DefaultAnonymousPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check user-configured anonymous paths
        foreach (var anonPath in _options.AnonymousPaths)
        {
            if (path.StartsWith(anonPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Files with extensions are usually static files — let them through
        var lastSegment = path.Split('/').LastOrDefault() ?? "";
        if (lastSegment.Contains('.') && !lastSegment.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            // Exception: .html files should still be authenticated
            // but .js, .css, .wasm, .dll, .json, .ico etc. should pass through
            return true;
        }

        return false;
    }

    private static bool IsApiRequest(HttpContext context)
    {
        // Check for common API indicators
        var path = context.Request.Path.Value ?? "";

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (context.Request.Headers.Accept.Any(a =>
            a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
            return true;

        if (context.Request.Headers.XRequestedWith.FirstOrDefault()
            ?.Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // Blazor WASM fetch calls typically send this header
        if (context.Request.Headers.ContainsKey("X-Requested-With"))
            return true;

        return false;
    }

    private string RemoveTokenFromQueryString(HttpContext context)
    {
        var query = context.Request.Query
            .Where(q => !q.Key.Equals(_options.TokenQueryParameterName, StringComparison.OrdinalIgnoreCase))
            .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}")
            .ToList();

        var path = context.Request.Path.Value ?? "/";
        return query.Count > 0 ? $"{path}?{string.Join("&", query)}" : path;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Enums
    // ═══════════════════════════════════════════════════════════════════

    private enum TokenValidationResult
    {
        Valid,
        ExpiringSoon,
        Expired,
        Invalid
    }

    private enum TokenSource
    {
        None,
        AuthorizationHeader,
        SessionCookie,
        LaunchCookie,
        QueryString
    }
}
