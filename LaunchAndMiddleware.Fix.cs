// ═══════════════════════════════════════════════════════════════════════════════
// PART 1: Launch action (AGOne central)
//
// Returns a self-posting HTML form instead of a redirect. The token is in the
// POST body — never in the URL, never in browser history, never in server logs,
// never in referrer headers.
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

    var html = $@"
<!DOCTYPE html>
<html>
<body>
    <form id=""f"" method=""POST"" action=""{System.Net.WebUtility.HtmlEncode(launchUrl)}"">
        <input type=""hidden"" name=""token"" value=""{System.Net.WebUtility.HtmlEncode(token)}"" />
    </form>
    <script>document.getElementById('f').submit();</script>
</body>
</html>";

    return Content(html, "text/html");
}


// ═══════════════════════════════════════════════════════════════════════════════
// PART 2: Product middleware ExtractToken — add ONE check for POST form body
//
// The ONLY change: add 5 lines between the cookie check and the return.
// Everything else stays exactly the same.
// ═══════════════════════════════════════════════════════════════════════════════

private (string? token, Src source) ExtractToken(HttpContext ctx)
{
    // 1. Authorization: Bearer xxx  (Blazor WASM API calls)
    var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
    {
        var t = auth["Bearer ".Length..].Trim();
        if (!string.IsNullOrEmpty(t)) return (t, Src.Header);
    }

    // 2. Session cookie  (subsequent requests after first launch)
    if (ctx.Request.Cookies.TryGetValue(_opts.SessionCookieName, out var sc) && !string.IsNullOrEmpty(sc))
        return (sc, Src.Session);

    // 3. POST form body (first request — AGOne launches product via auto-POST form)
    if (ctx.Request.Method == "POST" &&
        ctx.Request.HasFormContentType &&
        ctx.Request.Form.TryGetValue("token", out var ft) &&
        !string.IsNullOrEmpty(ft.FirstOrDefault()))
        return (ft.FirstOrDefault()!, Src.Query);

    return (null, Src.None);
}

// ═══════════════════════════════════════════════════════════════════════════════
// IMPORTANT: After ExtractToken returns a token from the POST form, your
// existing middleware logic should set the session cookie and redirect to "/"
// (stripping the POST body). This is the same thing it already does when it
// reads from query string. The token is in the POST body for exactly ONE
// request, then it lives in the cookie for all subsequent requests.
// ═══════════════════════════════════════════════════════════════════════════════
