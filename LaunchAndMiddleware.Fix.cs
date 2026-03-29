// ═══════════════════════════════════════════════════════════════════════════════
// FIX: Pass token via cookie instead of query string during product launch
//
// PROBLEM:
//   Launch redirects to product with ?token=xxx in the URL. This is insecure
//   (token visible in browser history, server logs, referrer headers) and
//   forces every product middleware to support query string token extraction.
//
// SOLUTION:
//   Launch redirects to a dedicated handoff endpoint on the product:
//     {productUrl}/.auth/handoff?t={encryptedToken}
//   That endpoint (mapped by the shared middleware) sets the session cookie
//   and redirects to the product root. The token is only in the URL for
//   one request to a dedicated endpoint, and the middleware removes query
//   string support for all other requests.
//
// CHANGES NEEDED:
//   1. AuthController.Launch (AGOne central) — redirect to /.auth/handoff
//   2. Product middleware — add handoff endpoint + remove query string logic
// ═══════════════════════════════════════════════════════════════════════════════


// ═══════════════════════════════════════════════════════════════════════════════
// PART 1: AuthController — Launch action (AGOne central app)
// ═══════════════════════════════════════════════════════════════════════════════

[HttpGet("launch/{productCode}")]
[AllowAnonymous]
public IActionResult Launch(string productCode, [FromQuery] string token)
{
    var launchUrl = _configuration[$"ProductLaunchUrls:{productCode}"];
    if (string.IsNullOrEmpty(launchUrl))
        return NotFound(new { message = $"Product '{productCode}' not found." });

    if (string.IsNullOrEmpty(token))
        return BadRequest(new { message = "Token is required." });

    if (productCode == "AGONEHIRE")
    {
        return Redirect(launchUrl);
    }

    var url = $"{launchUrl.TrimEnd('/')}/.auth/handoff?t={Uri.EscapeDataString(token)}";
    return Redirect(url);
}


// ═══════════════════════════════════════════════════════════════════════════════
// PART 2: Product middleware — updated ExtractToken + handoff endpoint
//
// Drop into your shared SSO middleware class that all 4 products use.
// ═══════════════════════════════════════════════════════════════════════════════

// ─── InvokeAsync — add handoff handling at the top ──────────────────────────

public async Task InvokeAsync(HttpContext ctx)
{
    // Handle token handoff from AGOne central
    if (ctx.Request.Path.StartsWithSegments("/.auth/handoff"))
    {
        HandleTokenHandoff(ctx);
        return;
    }

    // ... rest of your existing middleware logic ...
    var (token, source) = ExtractToken(ctx);
    // ...
}

// ─── HandleTokenHandoff — sets cookie, redirects clean ──────────────────────

private void HandleTokenHandoff(HttpContext ctx)
{
    var token = ctx.Request.Query["t"].FirstOrDefault();

    if (string.IsNullOrEmpty(token))
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    ctx.Response.Cookies.Append(_opts.SessionCookieName, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromHours(8)
    });

    ctx.Response.Redirect("/");
}

// ─── ExtractToken — cookie and header only, no query string ─────────────────

private (string? token, Src source) ExtractToken(HttpContext ctx)
{
    // 1. Authorization: Bearer xxx (Blazor WASM API calls)
    var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
    {
        var t = auth["Bearer ".Length..].Trim();
        if (!string.IsNullOrEmpty(t)) return (t, Src.Header);
    }

    // 2. Session cookie (set by handoff or previous request)
    if (ctx.Request.Cookies.TryGetValue(_opts.SessionCookieName, out var sc) && !string.IsNullOrEmpty(sc))
        return (sc, Src.Session);

    return (null, Src.None);
}
