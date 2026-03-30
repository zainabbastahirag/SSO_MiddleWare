// Replace your existing Launch action with this. No middleware changes needed.

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

    // Same domain → cookie is shared. Middleware already reads this cookie.
    Response.Cookies.Append("agone_session", token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromHours(8)
    });

    return Redirect(launchUrl);
}
