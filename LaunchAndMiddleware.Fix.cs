// ═══════════════════════════════════════════════════════════════════════════════
// Launch action (AGOne central)
//
// Since AGOne and AGOne Work are on the SAME domain (agone.aventragroup.com),
// cookies are shared. No need for POST forms or query string tokens.
//
// Flow:
//   1. Launch sets the session cookie (readable by the product middleware)
//   2. Redirects to the product URL with a plain GET (no token in URL)
//   3. Product middleware reads the token from the cookie — done
//
// This works because:
//   - Same domain = same cookie jar
//   - agone.aventragroup.com/api/auth/launch sets a cookie with Path=/
//   - agone.aventragroup.com/app-onework reads the same cookie
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

    // Set the session cookie that the product middleware will read.
    // Works because AGOne central and all products share the same domain.
    var cookieName = _configuration["AgOneSso:SessionCookieName"] ?? "agone_session";

    Response.Cookies.Append(cookieName, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromHours(8)
    });

    // Plain GET redirect — no token in URL, no POST form needed
    return Redirect(launchUrl);
}

// ═══════════════════════════════════════════════════════════════════════════════
// Product middleware ExtractToken — NO CHANGES NEEDED
//
// The middleware already reads from the session cookie:
//
//   if (ctx.Request.Cookies.TryGetValue(_opts.SessionCookieName, out var sc)
//       && !string.IsNullOrEmpty(sc))
//       return (sc, Src.Session);
//
// Since Launch set that same cookie (same domain, Path=/), the middleware
// will find the token in the cookie on the very first request to the product.
//
// You can now REMOVE the query string logic from ExtractToken if you want,
// since no product receives tokens via query string anymore.
// ═══════════════════════════════════════════════════════════════════════════════
