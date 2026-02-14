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
    public string LaunchCookieName { get; set; } = "agone_launch_token";
    public TimeSpan SessionCookieLifetime { get; set; } = TimeSpan.FromMinutes(60);
    public string? CookieDomain { get; set; }
    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.None;

    // ── Behavior ──
    public int RefreshBufferMinutes { get; set; } = 5;
    public bool IsAgOneGateway { get; set; } = false;
    public bool AcceptTokenFromQueryString { get; set; } = true;
    public string TokenQueryParameterName { get; set; } = "token";
    public List<string> AnonymousPaths { get; set; } = new();

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
    [JsonPropertyName("token")]   public string? Token { get; set; }
    [JsonPropertyName("idToken")] public string? IdToken { get; set; }
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

    // Framework paths that always pass through
    private static readonly string[] FrameworkPrefixes =
    {
        "/_framework", "/_content", "/_blazor", "/_vs",
        "/css", "/js", "/images", "/fonts", "/favicon.ico"
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

        // ── 2. Extract token (header → session cookie → launch cookie → query) ──
        var (token, source) = ExtractToken(ctx);

        if (string.IsNullOrEmpty(token))
        {
            await Reject(ctx, "No authentication token found");
            return;
        }

        // ── 3. Validate JWT ──
        var (principal, status) = await ValidateAsync(token);
        var activeToken = token;

        if (status == Status.ExpiringSoon)
        {
            // Still valid but expiring — try proactive refresh in background
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
                else { await Reject(ctx, "Refreshed token is invalid"); return; }
            }
            else
            {
                await Reject(ctx, "Token expired and refresh failed");
                return;
            }
        }
        else if (status == Status.Invalid)
        {
            await Reject(ctx, "Invalid token");
            return;
        }

        // ── 4. Set user identity ──
        if (principal != null) ctx.User = principal;

        // ── 5. Set/refresh session cookie ──
        SetSessionCookie(ctx, activeToken);

        // ── 6. Clean up launch sources ──
        if (source == Src.Launch)
            ctx.Response.Cookies.Delete(_opts.LaunchCookieName,
                new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.None });

        if (source == Src.Query)
        {
            // Redirect to clean URL (remove token from address bar)
            var clean = CleanQueryString(ctx);
            ctx.Response.Redirect(clean);
            return;
        }

        await _next(ctx);
    }

    // ═══════════ Token extraction ═══════════

    private enum Src { None, Header, Session, Launch, Query }

    private (string? token, Src source) ExtractToken(HttpContext ctx)
    {
        // 1. Authorization: Bearer xxx
        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var t = auth["Bearer ".Length..].Trim();
            if (!string.IsNullOrEmpty(t)) return (t, Src.Header);
        }

        // 2. Session cookie
        if (ctx.Request.Cookies.TryGetValue(_opts.SessionCookieName, out var sc) && !string.IsNullOrEmpty(sc))
            return (sc, Src.Session);

        // 3. Launch cookie (set by AG ONE Product Launcher)
        if (ctx.Request.Cookies.TryGetValue(_opts.LaunchCookieName, out var lc) && !string.IsNullOrEmpty(lc))
            return (lc, Src.Launch);

        // 4. Query string ?token=xxx
        if (_opts.AcceptTokenFromQueryString &&
            ctx.Request.Query.TryGetValue(_opts.TokenQueryParameterName, out var qt) &&
            !string.IsNullOrEmpty(qt.FirstOrDefault()))
            return (qt.FirstOrDefault()!, Src.Query);

        return (null, Src.None);
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
            else
            {
                // No OIDC config — parse without signature validation (dev/fallback)
                if (!_jwt.CanReadToken(token)) return (null, Status.Invalid);
                jwt = _jwt.ReadJwtToken(token);
                principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "AgOneSso"));
            }

            // Check expiry
            var now = DateTime.UtcNow;
            if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < now)
                return (principal, Status.Expired);
            if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < now.AddMinutes(_opts.RefreshBufferMinutes))
                return (principal, Status.ExpiringSoon);

            return (principal, Status.Valid);
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

    // ═══════════ Token refresh via AG ONE API ═══════════

    private async Task<string?> RefreshAsync(HttpContext ctx, string currentToken)
    {
        if (_opts.IsAgOneGateway) return null; // AG ONE doesn't call itself

        try
        {
            var client = ctx.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("AgOneSso");
            var resp = await client.PostAsJsonAsync(
                _opts.TokenValidateEndpoint.TrimStart('/'),
                new SsoTokenRequest { Token = currentToken },
                ctx.RequestAborted);

            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ctx.RequestAborted);
            var result = JsonSerializer.Deserialize<SsoTokenResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result?.Token; // null if validation failed on AG ONE side
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AG ONE token refresh failed");
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

        // POST /api/auth/sso-logout — clears SSO cookies
        endpoints.MapPost($"{p}/sso-logout", (HttpContext ctx, IOptions<AgOneSsoOptions> opts) =>
        {
            var o = opts.Value;
            ctx.Response.Cookies.Delete(o.SessionCookieName, new CookieOptions { Path = "/", Secure = true, SameSite = o.CookieSameSite });
            ctx.Response.Cookies.Delete(o.LaunchCookieName, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.None });
            return Results.Ok(new { loggedOut = true, redirectUrl = o.EffectiveLoginUrl });
        });

        return endpoints;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//
// SETUP INSTRUCTIONS
//
// ═══════════════════════════════════════════════════════════════════════════════
//
// ── FOR EVERY PRODUCT (AGOneWork, AGOneLearn, etc.) ──────────────────────────
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
// ── FOR AG ONE ITSELF ────────────────────────────────────────────────────────
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
// ── FOR BLAZOR WASM CLIENT (all products) ────────────────────────────────────
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
