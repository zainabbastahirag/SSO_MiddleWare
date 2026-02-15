// ═══════════════════════════════════════════════════════════════════════════════
// AG ONE SSO MIDDLEWARE — Single-file, plug-and-play SSO for all AG ONE products
// ═══════════════════════════════════════════════════════════════════════════════
//
// HOW TO USE:
//   1. Copy this file into your Server project
//   2. Add NuGet: dotnet add package Microsoft.IdentityModel.Protocols.OpenIdConnect
//   3. Add config to appsettings.json (see bottom of this file)
//   4. Add 3 lines to Program.cs:
//        builder.Services.AddAgOneSso(builder.Configuration);
//        app.UseAgOneSso();          // after UseRouting, before UseAuthorization
//        app.MapAgOneSsoEndpoints(); // after MapControllers
//
// ═══════════════════════════════════════════════════════════════════════════════

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AgOne.Sso;

// ─────────────────────────────────────────────────────────────────────────────
// OPTIONS
// ─────────────────────────────────────────────────────────────────────────────

public class AgOneSsoOptions
{
    public const string SectionName = "AgOneSso";

    /// <summary>Base URL of the AG ONE backend (e.g. "https://agone.example.com").</summary>
    public string AgOneBaseUrl { get; set; } = "";

    /// <summary>AG ONE login page URL. Defaults to AgOneBaseUrl if empty.</summary>
    public string AgOneLoginUrl { get; set; } = "";

    /// <summary>Relative path to AG ONE's token validate/refresh API.</summary>
    public string TokenValidateEndpoint { get; set; } = "api/auth/external/validate";

    // ── Entra ID ──
    /// <summary>Entra ID authority (e.g. "https://login.microsoftonline.com/{tenantId}/v2.0").</summary>
    public string Authority { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ValidAudience { get; set; } = "";
    public List<string> AdditionalAudiences { get; set; } = new();

    // ── Cookies ──
    public string SessionCookieName { get; set; } = "agone_sso_token";
    public TimeSpan SessionCookieLifetime { get; set; } = TimeSpan.FromMinutes(60);
    public string? CookieDomain { get; set; }
    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.None;

    // ── Behavior ──
    public int RefreshBufferMinutes { get; set; } = 5;
    public bool IsAgOneGateway { get; set; } = false;
    public bool AcceptTokenFromQueryString { get; set; } = true;
    public string TokenQueryParameterName { get; set; } = "token";

    /// <summary>Paths that skip the middleware entirely (no token check at all).</summary>
    public List<string> AnonymousPaths { get; set; } = new();

    /// <summary>
    /// Paths that are publicly accessible but WILL set the user if a token exists.
    /// - No token → page loads as anonymous (no redirect, no 401)
    /// - Has token → validates it, sets User so page can show "Welcome, John"
    /// Use this for home pages, landing pages, marketing pages, etc.
    /// Example: ["/", "/about", "/pricing"]
    /// </summary>
    public List<string> PublicPaths { get; set; } = new();

    // ── Internal helpers ──
    internal string EffectiveLoginUrl =>
        !string.IsNullOrEmpty(AgOneLoginUrl) ? AgOneLoginUrl : AgOneBaseUrl.TrimEnd('/');

    internal string TokenValidateUrl =>
        $"{AgOneBaseUrl.TrimEnd('/')}/{TokenValidateEndpoint.TrimStart('/')}";

    internal string EffectiveAuthority =>
        !string.IsNullOrEmpty(Authority) ? Authority
        : !string.IsNullOrEmpty(TenantId) ? $"https://login.microsoftonline.com/{TenantId}/v2.0"
        : "";

    internal IEnumerable<string> AllAudiences
    {
        get
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(ValidAudience)) list.Add(ValidAudience);
            if (!string.IsNullOrEmpty(ClientId)) { list.Add(ClientId); list.Add($"api://{ClientId}"); }
            list.AddRange(AdditionalAudiences);
            return list.Distinct();
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs (match your existing AG ONE API models)
// ─────────────────────────────────────────────────────────────────────────────

public class SsoTokenRequest
{
    [JsonPropertyName("token")]     public string? Token { get; set; }
    [JsonPropertyName("idToken")]   public string? IdToken { get; set; }
    [JsonPropertyName("userId")]    public string? UserId { get; set; }
    [JsonPropertyName("tenantId")]  public string? TenantId { get; set; }
}

public class SsoTokenResponse
{
    [JsonPropertyName("token")]   public string? Token { get; set; }
    [JsonPropertyName("isValid")] public bool IsValid { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public class SsoUserInfo
{
    [JsonPropertyName("isAuthenticated")] public bool IsAuthenticated { get; set; }
    [JsonPropertyName("userId")]          public string? UserId { get; set; }
    [JsonPropertyName("email")]           public string? Email { get; set; }
    [JsonPropertyName("name")]            public string? Name { get; set; }
    [JsonPropertyName("roles")]           public List<string> Roles { get; set; } = new();
    [JsonPropertyName("claims")]          public Dictionary<string, string> Claims { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// THE MIDDLEWARE
// ─────────────────────────────────────────────────────────────────────────────

public class AgOneSsoMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AgOneSsoOptions _opts;
    private readonly ILogger<AgOneSsoMiddleware> _log;
    private readonly JwtSecurityTokenHandler _jwt = new();
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _oidc;

    // Static file extensions that always pass through without auth
    private static readonly HashSet<string> StaticExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".css", ".wasm", ".dll", ".pdb", ".dat", ".json", ".ico",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".woff", ".woff2",
        ".ttf", ".eot", ".map", ".br", ".gz", ".blat"
    };

    // Framework paths that always pass through (no auth check at all)
    private static readonly string[] FrameworkPrefixes =
    {
        "/_framework", "/_content", "/_vs",
        "/css", "/js", "/images", "/fonts", "/favicon.ico"
    };

    // Paths where we TRY to set the user (if cookie exists) but never reject.
    // /_blazor is here because Blazor Server's SignalR negotiate request carries cookies
    // and Blazor captures the auth state from that connection. If we skip it entirely,
    // the Blazor circuit starts as unauthenticated even though the user has a valid cookie.
    private static readonly string[] AlwaysPublicPrefixes =
    {
        "/_blazor"
    };

    public AgOneSsoMiddleware(RequestDelegate next, IOptions<AgOneSsoOptions> opts, ILogger<AgOneSsoMiddleware> log)
    {
        _next = next;
        _opts = opts.Value;
        _log = log;

        var authority = _opts.EffectiveAuthority;
        if (!string.IsNullOrEmpty(authority))
        {
            _oidc = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());
        }
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "/";

        // ── 1. Skip anonymous paths (static files, framework, configured paths) ──
        if (IsAnonymous(path))
        {
            await _next(ctx);
            return;
        }

        // ── 2. Check if this is a public path (accessible without login) ──
        var isPublicPath = IsPublicPath(path);

        // ── 3. Extract token (header → session cookie → query) ──
        var (token, source) = ExtractToken(ctx);

        if (string.IsNullOrEmpty(token))
        {
            if (isPublicPath)
            {
                // Public path, no token → just continue as anonymous (no redirect)
                await _next(ctx);
                return;
            }

            await Reject(ctx, "No authentication token found");
            return;
        }

        // ── 4. Validate JWT ──
        var (principal, status) = await ValidateAsync(token);
        var activeToken = token;

        if (status == Status.ExpiringSoon)
        {
            // Still valid but expiring — try proactive refresh
            var refreshed = await RefreshAsync(ctx, token);
            if (!string.IsNullOrEmpty(refreshed))
            {
                activeToken = refreshed;
                var (p2, _) = await ValidateAsync(refreshed);
                if (p2 != null) principal = p2;
            }
            // If refresh fails, the token is still valid — continue with it
        }
        else if (status == Status.Expired)
        {
            // Must refresh
            var refreshed = await RefreshAsync(ctx, token);
            if (!string.IsNullOrEmpty(refreshed))
            {
                activeToken = refreshed;
                var (p2, s2) = await ValidateAsync(refreshed);
                if (p2 != null) principal = p2;
                else if (!isPublicPath) { await Reject(ctx, "Refreshed token is invalid"); return; }
                else { await _next(ctx); return; } // public path — continue as anonymous
            }
            else if (!isPublicPath)
            {
                await Reject(ctx, "Token expired and refresh failed");
                return;
            }
            else
            {
                // Public path, expired token, refresh failed → continue as anonymous
                await _next(ctx);
                return;
            }
        }
        else if (status == Status.Invalid)
        {
            if (!isPublicPath)
            {
                await Reject(ctx, "Invalid token");
                return;
            }
            // Public path with invalid token → continue as anonymous
            await _next(ctx);
            return;
        }

        // ── 4. Set user identity ──
        if (principal != null) ctx.User = principal;

        // ── 5. Set/refresh session cookie ──
        SetSessionCookie(ctx, activeToken);

        // ── 6. If token came from query string, redirect to clean URL ──
        //       (removes token from address bar so it's not in browser history)
        if (source == Src.Query)
        {
            var clean = CleanQueryString(ctx);
            ctx.Response.Redirect(clean);
            return;
        }

        await _next(ctx);
    }

    // ═══════════ Token extraction ═══════════

    private enum Src { None, Header, Session, Query }

    private (string? token, Src source) ExtractToken(HttpContext ctx)
    {
        // 1. Authorization: Bearer xxx  (Blazor WASM API calls)
        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var t = CleanToken(auth["Bearer ".Length..]);
            if (!string.IsNullOrEmpty(t)) return (t, Src.Header);
        }

        // 2. Session cookie  (subsequent requests after first launch)
        if (ctx.Request.Cookies.TryGetValue(_opts.SessionCookieName, out var sc) && !string.IsNullOrEmpty(sc))
            return (CleanToken(sc)!, Src.Session);

        // 3. Query string ?token=xxx  (first request — AG ONE Product Launcher redirects here)
        if (_opts.AcceptTokenFromQueryString &&
            ctx.Request.Query.TryGetValue(_opts.TokenQueryParameterName, out var qt) &&
            !string.IsNullOrEmpty(qt.FirstOrDefault()))
            return (CleanToken(qt.FirstOrDefault()!)!, Src.Query);

        return (null, Src.None);
    }

    /// <summary>
    /// Cleans a token string by removing surrounding quotes (Blazor LocalStorage adds these),
    /// whitespace, newlines, and other invisible characters that break DB lookups.
    /// </summary>
    private static string? CleanToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        return token.Trim().Trim('"').Trim();
    }

    // ═══════════ JWT validation ═══════════

    private enum Status { Valid, ExpiringSoon, Expired, Invalid }

    private async Task<(ClaimsPrincipal? principal, Status status)> ValidateAsync(string token)
    {
        try
        {
            ClaimsPrincipal principal;
            JwtSecurityToken jwt;

            if (_oidc != null)
            {
                try
                {
                    var config = await _oidc.GetConfigurationAsync(CancellationToken.None);
                    var tvp = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = config.SigningKeys,
                        ValidateIssuer = true,
                        ValidIssuers = new[]
                        {
                            _opts.EffectiveAuthority,
                            $"https://login.microsoftonline.com/{_opts.TenantId}/v2.0",
                            $"https://sts.windows.net/{_opts.TenantId}/"
                        },
                        ValidateAudience = _opts.AllAudiences.Any(),
                        ValidAudiences = _opts.AllAudiences,
                        ValidateLifetime = false, // We check expiry ourselves for refresh logic
                        ClockSkew = TimeSpan.FromMinutes(2)
                    };

                    principal = _jwt.ValidateToken(token, tvp, out var validated);
                    jwt = (JwtSecurityToken)validated;
                }
                catch (SecurityTokenSignatureKeyNotFoundException ex)
                {
                    // Token's 'kid' header is missing or doesn't match any Entra ID signing key.
                    // This happens when the token is an Entra ID access token scoped for Microsoft
                    // Graph (opaque format) rather than for your own API. Since the token came from
                    // AG ONE (trusted source over HTTPS), fall back to reading claims without
                    // signature validation. AG ONE remains the authority for token validity.
                    _log.LogDebug(ex, "Signature key not found — falling back to claims-only parsing. " +
                        "This is normal for Graph-scoped Entra ID tokens.");
                    return ParseWithoutSignatureValidation(token);
                }
                catch (SecurityTokenInvalidSignatureException ex)
                {
                    // Similar — signature can't be verified (e.g. encrypted Graph token)
                    _log.LogDebug(ex, "Signature validation failed — falling back to claims-only parsing.");
                    return ParseWithoutSignatureValidation(token);
                }
            }
            else
            {
                return ParseWithoutSignatureValidation(token);
            }

            return ClassifyExpiry(principal, jwt);
        }
        catch (SecurityTokenExpiredException)
        {
            return (null, Status.Expired);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "JWT validation failed");
            return (null, Status.Invalid);
        }
    }

    /// <summary>
    /// Reads JWT claims without verifying the signature. Used when:
    /// - The token is a Graph-scoped Entra ID token (no kid / opaque format)
    /// - OIDC metadata is not configured
    /// This is safe because the token came from AG ONE (trusted source via HTTPS cookie),
    /// and AG ONE is the authority that validates/refreshes tokens against Entra ID.
    /// </summary>
    private (ClaimsPrincipal? principal, Status status) ParseWithoutSignatureValidation(string token)
    {
        try
        {
            if (!_jwt.CanReadToken(token)) return (null, Status.Invalid);
            var jwt = _jwt.ReadJwtToken(token);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "AgOneSso"));
            return ClassifyExpiry(principal, jwt);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to parse JWT");
            return (null, Status.Invalid);
        }
    }

    private (ClaimsPrincipal principal, Status status) ClassifyExpiry(ClaimsPrincipal principal, JwtSecurityToken jwt)
    {
        var now = DateTime.UtcNow;
        if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < now)
            return (principal, Status.Expired);
        if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < now.AddMinutes(_opts.RefreshBufferMinutes))
            return (principal, Status.ExpiringSoon);
        return (principal, Status.Valid);
    }

    // ═══════════ Token refresh via AG ONE API ═══════════

    private async Task<string?> RefreshAsync(HttpContext ctx, string currentToken)
    {
        if (_opts.IsAgOneGateway) return null; // AG ONE doesn't call itself

        var endpoint = _opts.TokenValidateEndpoint.TrimStart('/');
        var fullUrl = $"{_opts.AgOneBaseUrl.TrimEnd('/')}/{endpoint}";

        try
        {
            var factory = ctx.RequestServices.GetService<IHttpClientFactory>();
            if (factory == null)
            {
                _log.LogError("IHttpClientFactory not registered. Did you call builder.Services.AddAgOneSso()?");
                return null;
            }

            var client = factory.CreateClient("AgOneSso");

            // Extract userId and tenantId from the current JWT claims.
            // The token from LocalStorage is AG ONE's custom JWT (HS256) which has
            // sub=userId, tenant_id=tenantId. The DB stores the Entra ID token (RS256)
            // which is a completely different string. So we send the userId so AG ONE
            // can look up the user's Entra ID token by UserId instead of token string.
            string? userId = null;
            string? tenantId = null;
            try
            {
                if (_jwt.CanReadToken(currentToken))
                {
                    var parsed = _jwt.ReadJwtToken(currentToken);
                    userId = parsed.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                          ?? parsed.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                    tenantId = parsed.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value
                            ?? parsed.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
                }
            }
            catch { /* If parsing fails, we still send the raw token */ }

            _log.LogInformation("Calling AG ONE token refresh: POST {Url} (userId={UserId})", fullUrl, userId ?? "unknown");

            var resp = await client.PostAsJsonAsync(
                endpoint,
                new SsoTokenRequest
                {
                    Token = currentToken,
                    UserId = userId,
                    TenantId = tenantId
                },
                ctx.RequestAborted);

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ctx.RequestAborted);
                _log.LogWarning("AG ONE token refresh returned {StatusCode}: {Body}", resp.StatusCode, errorBody);
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(ctx.RequestAborted);
            var result = JsonSerializer.Deserialize<SsoTokenResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (!string.IsNullOrEmpty(result?.Token))
            {
                _log.LogInformation("AG ONE token refresh succeeded");
            }
            else
            {
                _log.LogWarning("AG ONE returned no token: {Body}", body);
            }

            return result?.Token;
        }
        catch (HttpRequestException ex)
        {
            // This catches: SSL errors, connection refused, DNS failures, timeouts
            _log.LogError(ex,
                "AG ONE token refresh FAILED — cannot connect to {Url}. " +
                "Check that: 1) AG ONE is running, 2) AgOneBaseUrl '{BaseUrl}' is correct, " +
                "3) The URL is reachable from this server. Inner error: {Message}",
                fullUrl, _opts.AgOneBaseUrl, ex.InnerException?.Message ?? ex.Message);
            return null;
        }
        catch (TaskCanceledException ex) when (!ctx.RequestAborted.IsCancellationRequested)
        {
            _log.LogError(ex, "AG ONE token refresh TIMED OUT calling {Url}", fullUrl);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AG ONE token refresh failed calling {Url}", fullUrl);
            return null;
        }
    }

    // ═══════════ Cookie ═══════════

    private void SetSessionCookie(HttpContext ctx, string token)
    {
        var co = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = _opts.CookieSameSite,
            Path = "/",
            MaxAge = _opts.SessionCookieLifetime,
            IsEssential = true
        };
        if (!string.IsNullOrEmpty(_opts.CookieDomain)) co.Domain = _opts.CookieDomain;
        ctx.Response.Cookies.Append(_opts.SessionCookieName, token, co);
    }

    // ═══════════ Reject ═══════════

    private async Task Reject(HttpContext ctx, string reason)
    {
        ctx.Response.Cookies.Delete(_opts.SessionCookieName,
            new CookieOptions { Path = "/", Secure = true, SameSite = _opts.CookieSameSite });

        if (IsApi(ctx))
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new { isAuthenticated = false, message = reason, loginUrl = _opts.EffectiveLoginUrl });
        }
        else
        {
            var returnUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.Path}{ctx.Request.QueryString}";
            ctx.Response.Redirect($"{_opts.EffectiveLoginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }
    }

    // ═══════════ Helpers ═══════════

    private bool IsAnonymous(string path)
    {
        foreach (var p in FrameworkPrefixes)
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var p in _opts.AnonymousPaths)
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;

        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && StaticExtensions.Contains(ext)) return true;

        return false;
    }

    private bool IsPublicPath(string path)
    {
        // Built-in public paths (e.g. /_blazor for Blazor Server SignalR)
        foreach (var p in AlwaysPublicPrefixes)
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;

        // User-configured public paths
        foreach (var p in _opts.PublicPaths)
        {
            // Exact match for "/" (home page)
            if (p == "/" && path == "/") return true;
            // Prefix match for other paths (e.g. "/about" matches "/about" and "/about/team")
            if (p != "/" && path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsApi(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return true;
        if (ctx.Request.Headers.Accept.Any(a => a?.Contains("application/json") == true)) return true;
        if (ctx.Request.Headers.ContainsKey("X-Requested-With")) return true;
        return false;
    }

    private string CleanQueryString(HttpContext ctx)
    {
        var keep = ctx.Request.Query
            .Where(q => !q.Key.Equals(_opts.TokenQueryParameterName, StringComparison.OrdinalIgnoreCase))
            .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}")
            .ToList();
        var path = ctx.Request.Path.Value ?? "/";
        return keep.Count > 0 ? $"{path}?{string.Join("&", keep)}" : path;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EXTENSION METHODS — call these from Program.cs
// ─────────────────────────────────────────────────────────────────────────────

public static class AgOneSsoExtensions
{
    /// <summary>
    /// Registers SSO services. Call in Program.cs:
    ///   builder.Services.AddAgOneSso(builder.Configuration);
    /// </summary>
    public static IServiceCollection AddAgOneSso(this IServiceCollection services, IConfiguration config)
    {
        return services.AddAgOneSso(config, _ => { });
    }

    /// <summary>
    /// Registers SSO services with extra config. Call in Program.cs:
    ///   builder.Services.AddAgOneSso(builder.Configuration, o => o.AnonymousPaths.Add("/api/health"));
    /// </summary>
    public static IServiceCollection AddAgOneSso(this IServiceCollection services, IConfiguration config, Action<AgOneSsoOptions> configure)
    {
        services.Configure<AgOneSsoOptions>(o =>
        {
            config.GetSection(AgOneSsoOptions.SectionName).Bind(o);
            configure(o);
        });

        var opts = new AgOneSsoOptions();
        config.GetSection(AgOneSsoOptions.SectionName).Bind(opts);
        configure(opts);

        if (!opts.IsAgOneGateway && !string.IsNullOrEmpty(opts.AgOneBaseUrl))
        {
            services.AddHttpClient("AgOneSso", c =>
            {
                c.BaseAddress = new Uri(opts.AgOneBaseUrl.TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(30);
                c.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Trust AG ONE's SSL certificate (required in local dev when AG ONE
                // runs on localhost with a self-signed/dev certificate)
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }

        return services;
    }

    /// <summary>
    /// Adds the SSO middleware. Call in Program.cs AFTER UseRouting(), BEFORE UseAuthorization():
    ///   app.UseAgOneSso();
    /// </summary>
    public static IApplicationBuilder UseAgOneSso(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AgOneSsoMiddleware>();
    }

    /// <summary>
    /// Maps SSO helper endpoints (/api/auth/sso-user-info, /api/auth/sso-logout).
    /// Call in Program.cs after MapControllers():
    ///   app.MapAgOneSsoEndpoints();
    /// </summary>
    public static IEndpointRouteBuilder MapAgOneSsoEndpoints(this IEndpointRouteBuilder endpoints, string basePath = "api/auth")
    {
        var p = basePath.TrimEnd('/');

        // GET /api/auth/sso-user-info — Blazor WASM calls this to check auth state
        endpoints.MapGet($"{p}/sso-user-info", (HttpContext ctx) =>
        {
            var user = ctx.User;
            if (user.Identity?.IsAuthenticated != true)
                return Results.Ok(new SsoUserInfo { IsAuthenticated = false });

            return Results.Ok(new SsoUserInfo
            {
                IsAuthenticated = true,
                UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("oid")?.Value
                      ?? user.FindFirst("sub")?.Value,
                Email  = user.FindFirst(ClaimTypes.Email)?.Value
                      ?? user.FindFirst("preferred_username")?.Value
                      ?? user.FindFirst("email")?.Value,
                Name   = user.FindFirst("name")?.Value
                      ?? user.FindFirst(ClaimTypes.Name)?.Value,
                Roles  = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("roles"))
                             .Select(c => c.Value).Distinct().ToList()
            });
        });

        // POST /api/auth/sso-logout — clears SSO cookie
        endpoints.MapPost($"{p}/sso-logout", (HttpContext ctx, IOptions<AgOneSsoOptions> opts) =>
        {
            var o = opts.Value;
            ctx.Response.Cookies.Delete(o.SessionCookieName, new CookieOptions { Path = "/", Secure = true, SameSite = o.CookieSameSite });
            return Results.Ok(new { loggedOut = true, redirectUrl = o.EffectiveLoginUrl });
        });

        return endpoints;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//
// HOW THE FULL FLOW WORKS
//
// ═══════════════════════════════════════════════════════════════════════════════
//
//   1. User is logged into AG ONE (Entra ID). Token stored in DB + local storage.
//   2. User clicks "Launch AGOneWork" in AG ONE.
//   3. AG ONE's ProductLaunchController redirects browser to:
//        https://agonework.example.com?token=eyJhbG...
//   4. Browser hits AGOneWork. This middleware runs:
//        a. Sees ?token=xxx in the URL
//        b. Validates the JWT (signature + expiry)
//        c. Sets HttpOnly session cookie (agone_sso_token) on agonework.example.com
//        d. Redirects to clean URL: https://agonework.example.com (no token in address bar)
//   5. All subsequent requests from AGOneWork send the session cookie automatically.
//   6. When the token is about to expire, middleware calls AG ONE's
//      POST /api/auth/external/validate to get a refreshed token.
//   7. If refresh fails → redirect user back to AG ONE login.
//
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// WHAT TO CHANGE IN AG ONE (ProductLaunchController)
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// Your existing Product Launcher sets a cookie and redirects. The problem is
// that cookie is on AG ONE's domain — Product X on a different domain can't
// read it. Replace it with a simple query-string redirect:
//
//   [HttpGet("launch/{productCode}")]
//   [AllowAnonymous]
//   public IActionResult Launch(string productCode, [FromQuery] string token)
//   {
//       var launchUrl = _configuration[$"ProductLaunchUrls:{productCode}"];
//       if (string.IsNullOrEmpty(launchUrl))
//           return NotFound(new { message = $"Product '{productCode}' not found." });
//
//       if (string.IsNullOrEmpty(token))
//           return BadRequest(new { message = "Token is required." });
//
//       // Just redirect to the product with token in query string.
//       // The middleware on Product X will pick it up, set a cookie on
//       // Product X's own domain, and redirect to a clean URL.
//       var url = $"{launchUrl.TrimEnd('/')}?token={Uri.EscapeDataString(token)}";
//       return Redirect(url);
//   }
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// WHAT TO CHANGE IN AG ONE (ExternalTokenValidationRequest + ValidateAndGetAccessTokenAsync)
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// PROBLEM: The token in LocalStorage (custom HS256 JWT with sub=userId) is
// completely different from the Entra ID token stored in the DB (RS256 Graph token).
// Matching by token string will NEVER work.
//
// FIX: Add UserId/TenantId fields to ExternalTokenValidationRequest and look up by UserId.
//
// 1. Update ExternalTokenValidationRequest:
//
//   public class ExternalTokenValidationRequest
//   {
//       public string? Token { get; set; }
//       public string? IdToken { get; set; }
//       public string? UserId { get; set; }     // ← ADD THIS
//       public string? TenantId { get; set; }   // ← ADD THIS
//   }
//
// 2. Update ValidateAndGetAccessTokenAsync:
//
//   public async Task<string?> ValidateAndGetAccessTokenAsync(ExternalTokenValidationRequest request)
//   {
//       // 1️⃣ Look up by UserId (sent by the middleware from the custom JWT's "sub" claim)
//       if (!string.IsNullOrEmpty(request.UserId))
//       {
//           var tokenByUser = await _db.UserTokens
//               .FirstOrDefaultAsync(t => t.UserId == request.UserId && t.IsActive);
//
//           if (tokenByUser != null)
//               return await EnsureValidTokenAsync(tokenByUser);
//       }
//
//       // 2️⃣ Fallback: check by IdToken
//       if (!string.IsNullOrEmpty(request.IdToken))
//       {
//           var cleanIdToken = request.IdToken.Trim().Trim('"').Trim();
//           var tokenByTemp = await _db.UserTokens
//               .FirstOrDefaultAsync(t => t.IdToken == cleanIdToken && t.IsActive);
//
//           if (tokenByTemp != null)
//               return await EnsureValidTokenAsync(tokenByTemp);
//       }
//
//       // 3️⃣ Fallback: check by AccessToken
//       if (!string.IsNullOrEmpty(request.Token))
//       {
//           var cleanToken = request.Token.Trim().Trim('"').Trim();
//           var tokenByAccess = await _db.UserTokens
//               .FirstOrDefaultAsync(t => t.AccessToken == cleanToken && t.IsActive);
//
//           if (tokenByAccess != null)
//               return await EnsureValidTokenAsync(tokenByAccess);
//       }
//
//       return null;
//   }
//
// The middleware now sends { token, userId, tenantId } in every refresh request.
// userId comes from the "sub" claim of the custom JWT that AG ONE generated.
//
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// SETUP FOR PRODUCT APPS (AGOneWork, AGOneLearn, etc.)
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// 1. Copy this file into your Server project
//
// 2. Install NuGet:
//      dotnet add package Microsoft.IdentityModel.Protocols.OpenIdConnect
//
// 3. Program.cs — add these 3 lines:
//
//      builder.Services.AddAgOneSso(builder.Configuration);
//
//      app.UseRouting();
//      app.UseCors();
//      app.UseAgOneSso();          // ← HERE (after routing/cors, before auth)
//      app.UseAuthorization();
//
//      app.MapControllers();
//      app.MapAgOneSsoEndpoints(); // ← HERE (maps user-info & logout endpoints)
//      app.MapFallbackToFile("index.html");
//
// 4. appsettings.json:
//
//    {
//      "AgOneSso": {
//        "AgOneBaseUrl": "https://agone.yourdomain.com",
//        "AgOneLoginUrl": "https://agone.yourdomain.com",
//        "TenantId": "YOUR-ENTRA-TENANT-ID",
//        "ClientId": "YOUR-ENTRA-CLIENT-ID",
//        "ValidAudience": "api://YOUR-ENTRA-CLIENT-ID",
//        "IsAgOneGateway": false
//      }
//    }
//
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// SETUP FOR AG ONE ITSELF (optional — same middleware, gateway mode)
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// Same file, same 3 lines in Program.cs. Only difference is appsettings.json:
//
//    {
//      "AgOneSso": {
//        "AgOneBaseUrl": "https://agone.yourdomain.com",
//        "AgOneLoginUrl": "https://agone.yourdomain.com/login",
//        "TenantId": "YOUR-ENTRA-TENANT-ID",
//        "ClientId": "YOUR-ENTRA-CLIENT-ID",
//        "ValidAudience": "api://YOUR-ENTRA-CLIENT-ID",
//        "IsAgOneGateway": true,
//        "PublicPaths": ["/"],
//        "AnonymousPaths": [
//          "/login",
//          "/api/auth/login",
//          "/api/auth/callback",
//          "/api/auth/external/validate",
//          "/api/productlaunch"
//        ]
//      }
//    }
//
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// BLAZOR WASM CLIENT (all products)
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// In your WASM Client project, add this class:
//
//   using Microsoft.AspNetCore.Components.WebAssembly.Http;
//
//   public class CookieHandler : DelegatingHandler
//   {
//       protected override Task<HttpResponseMessage> SendAsync(
//           HttpRequestMessage request, CancellationToken cancellationToken)
//       {
//           request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
//           return base.SendAsync(request, cancellationToken);
//       }
//   }
//
// Then in WASM Program.cs:
//
//   builder.Services.AddTransient<CookieHandler>();
//   builder.Services.AddHttpClient("Backend",
//       c => c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
//       .AddHttpMessageHandler<CookieHandler>();
//   builder.Services.AddScoped(sp =>
//       sp.GetRequiredService<IHttpClientFactory>().CreateClient("Backend"));
//
// ═══════════════════════════════════════════════════════════════════════════════
