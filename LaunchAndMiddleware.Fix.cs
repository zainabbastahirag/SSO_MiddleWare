// ═══════════════════════════════════════════════════════════════════════════════
// PART 1: Launch action (AGOne central) — auto-POST form to /auth/token
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
        return Redirect(launchUrl);

    // POST to the product's dedicated token endpoint — not the root
    var postUrl = $"{launchUrl.TrimEnd('/')}/auth/token";

    var html = $@"<!DOCTYPE html>
<html><body>
<form id=""f"" method=""POST"" action=""{System.Net.WebUtility.HtmlEncode(postUrl)}"">
<input type=""hidden"" name=""token"" value=""{System.Net.WebUtility.HtmlEncode(token)}""/>
</form>
<script>document.getElementById('f').submit();</script>
</body></html>";

    return Content(html, "text/html");
}


// ═══════════════════════════════════════════════════════════════════════════════
// PART 2: Product middleware — add this at the TOP of InvokeAsync
//
// This is the only middleware change. 10 lines. It catches the POST,
// sets the cookie, and redirects to "/" which loads the Blazor app normally.
// ═══════════════════════════════════════════════════════════════════════════════

// Add this at the very beginning of your InvokeAsync method:

if (ctx.Request.Path.StartsWithSegments("/auth/token") &&
    ctx.Request.Method == "POST" &&
    ctx.Request.HasFormContentType)
{
    var token = ctx.Request.Form["token"].FirstOrDefault();
    if (!string.IsNullOrEmpty(token))
    {
        ctx.Response.Cookies.Append(_opts.SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromHours(8)
        });
    }
    ctx.Response.Redirect("/");
    return;
}

// ... rest of your existing InvokeAsync continues here ...
