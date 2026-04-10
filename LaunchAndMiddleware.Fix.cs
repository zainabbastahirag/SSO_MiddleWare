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
// PART 2: Add this controller to each product app. Zero middleware changes.
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("auth")]
[AllowAnonymous]
public class TokenHandoffController : Controller
{
    [HttpPost("token")]
    public IActionResult ReceiveToken([FromForm] string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            Response.Cookies.Append("agone_session", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.FromHours(8)
            });
        }

        return Redirect("/");
    }
}
