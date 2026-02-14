using System.Security.Claims;
using AgOne.Sso.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgOne.Sso.Extensions;

/// <summary>
/// Extension methods for mapping AG ONE SSO API endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the SSO auth info endpoint that Blazor WASM calls to get the current user's claims.
    /// Also maps a logout endpoint to clear the SSO session cookie.
    /// 
    /// <code>
    /// // In Program.cs (after MapControllers or similar):
    /// app.MapAgOneSsoEndpoints();
    /// </code>
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="basePath">Base path for the SSO endpoints. Defaults to "api/auth".</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapAgOneSsoEndpoints(
        this IEndpointRouteBuilder endpoints,
        string basePath = "api/auth")
    {
        var path = basePath.TrimEnd('/');

        // ─── User Info Endpoint ───
        // Called by Blazor WASM's AuthenticationStateProvider to get current user claims
        endpoints.MapGet($"{path}/sso-user-info", (HttpContext context) =>
        {
            var user = context.User;

            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Ok(new SsoUserInfo { IsAuthenticated = false });
            }

            var userInfo = new SsoUserInfo
            {
                IsAuthenticated = true,
                UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("oid")?.Value
                      ?? user.FindFirst("sub")?.Value,
                Email = user.FindFirst(ClaimTypes.Email)?.Value
                     ?? user.FindFirst("preferred_username")?.Value
                     ?? user.FindFirst("email")?.Value
                     ?? user.FindFirst("upn")?.Value,
                Name = user.FindFirst("name")?.Value
                    ?? user.FindFirst(ClaimTypes.Name)?.Value
                    ?? user.FindFirst("given_name")?.Value,
                Roles = user.FindAll(ClaimTypes.Role)
                    .Concat(user.FindAll("roles"))
                    .Select(c => c.Value)
                    .Distinct()
                    .ToList()
            };

            // Include additional useful claims
            var importantClaims = new[] { "tid", "aud", "iss", "tenant_id", "department", "job_title" };
            foreach (var claimType in importantClaims)
            {
                var claim = user.FindFirst(claimType);
                if (claim != null && !userInfo.Claims.ContainsKey(claimType))
                {
                    userInfo.Claims[claimType] = claim.Value;
                }
            }

            return Results.Ok(userInfo);
        })
        .WithName("AgOneSsoUserInfo")
        .WithTags("AgOne SSO");

        // ─── Logout Endpoint ───
        // Clears the SSO session cookie and optionally redirects to AG ONE login
        endpoints.MapPost($"{path}/sso-logout", (HttpContext context, IOptions<AgOneSsoOptions> options) =>
        {
            var opts = options.Value;

            // Clear session cookie
            context.Response.Cookies.Delete(opts.SessionCookieName, new CookieOptions
            {
                Path = "/",
                Secure = true,
                SameSite = opts.CookieSameSite
            });

            // Clear launch cookie if present
            context.Response.Cookies.Delete(opts.LaunchCookieName, new CookieOptions
            {
                Path = "/",
                Secure = true,
                SameSite = SameSiteMode.None
            });

            return Results.Ok(new
            {
                loggedOut = true,
                redirectUrl = opts.EffectiveLoginUrl
            });
        })
        .WithName("AgOneSsoLogout")
        .WithTags("AgOne SSO");

        // ─── Health Check Endpoint ───
        // Quick check that the SSO middleware is configured and running
        endpoints.MapGet($"{path}/sso-health", () =>
        {
            return Results.Ok(new
            {
                status = "healthy",
                middleware = "AgOne.Sso",
                timestamp = DateTime.UtcNow
            });
        })
        .WithName("AgOneSsoHealth")
        .WithTags("AgOne SSO");

        return endpoints;
    }
}
