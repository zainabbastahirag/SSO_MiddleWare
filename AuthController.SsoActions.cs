// ═══════════════════════════════════════════════════════════════════════════════
// AuthController SSO Actions — REPLACEMENT for the manual token exchange
//
// HOW IT WORKS NOW:
//
//   The OIDC middleware in Program.cs handles the ENTIRE authorization_code +
//   PKCE flow automatically:
//     1. LoginWithMicrosoft calls Challenge() → middleware generates PKCE
//        code_verifier/code_challenge, sets correlation + nonce cookies,
//        and redirects the browser to Microsoft.
//     2. Microsoft redirects back to /signin-oidc with ?code=...&state=...
//     3. The OIDC middleware intercepts /signin-oidc, exchanges the code
//        for tokens (using the code_verifier it stored), validates them,
//        creates a ClaimsPrincipal, signs in the user via the Cookie scheme,
//        and redirects to the configured RedirectUri.
//     4. HandleSsoComplete runs on that final redirect. The user is already
//        authenticated — HttpContext.User has all claims, and the tokens
//        are stored in the auth properties (because SaveTokens = true).
//
//   You do NOT need ExchangeCodeForTokensAsync, PKCE storage, or any
//   manual HTTP calls to the token endpoint.
//
// DROP THESE TWO ACTIONS INTO YOUR EXISTING AuthController CLASS.
// You can remove the old HandleSsoCallback and ExchangeCodeForTokensAsync.
// ═══════════════════════════════════════════════════════════════════════════════

// ─── ACTION 1: Kick off the Microsoft SSO login ─────────────────────────────

[AllowAnonymous]
[HttpGet("login/microsoft")]
public IActionResult LoginWithMicrosoft(
    [FromQuery] string? returnUrl = null,
    [FromQuery] string? role = null,
    [FromQuery] string? productId = null)
{
    var properties = new AuthenticationProperties
    {
        RedirectUri = Url.Action(nameof(HandleSsoComplete), "Auth",
            new { returnUrl, role, productId },
            Request.Scheme)
    };

    return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
}

// ─── ACTION 2: Runs AFTER the middleware has completed the token exchange ────

[Authorize(AuthenticationSchemes = "Cookies")]
[HttpGet("sso/complete")]
public async Task<IActionResult> HandleSsoComplete(
    [FromQuery] string? returnUrl = null,
    [FromQuery] string? role = null,
    [FromQuery] string? productId = null)
{
    var email = User.FindFirst("preferred_username")?.Value
             ?? User.FindFirst("email")?.Value
             ?? User.FindFirst("emails")?.Value;

    var objectId = User.FindFirst("oid")?.Value
                ?? User.FindFirst("sub")?.Value;

    var tenantId = User.FindFirst("tid")?.Value;
    var displayName = User.FindFirst("name")?.Value;

    if (string.IsNullOrEmpty(objectId))
    {
        _logger.LogError("SSO complete but no 'oid' or 'sub' claim found in the token");
        return BadRequest("Authentication succeeded but user identity could not be determined.");
    }

    _logger.LogInformation("SSO complete — Email: {Email}, Oid: {Oid}, Tid: {Tid}",
        email, objectId, tenantId);

    // Read tokens that the OIDC middleware stored (SaveTokens = true)
    var accessToken  = await HttpContext.GetTokenAsync("access_token");
    var idToken      = await HttpContext.GetTokenAsync("id_token");
    var refreshToken = await HttpContext.GetTokenAsync("refresh_token");
    var expiresAt    = await HttpContext.GetTokenAsync("expires_at");

    var isNewUser = !await _userService.UserExistsByObjectIdOrEmailAsync(objectId, email);
    var defaultRole = role ?? (isNewUser ? "TenantAdmin" : null);

    _logger.LogInformation("IsNewUser: {IsNew}, Role: {Role}, ProductId: {ProductId}",
        isNewUser, defaultRole, productId ?? "(none)");

    // Issue your own application JWT so the SPA/client can call your APIs
    var appToken = await _tokenService.GenerateTokenAsync(new TokenRequest
    {
        ObjectId = objectId,
        Email = email ?? "",
        DisplayName = displayName ?? "",
        TenantId = tenantId,
        Role = defaultRole,
        ProductId = productId,
        IsNewUser = isNewUser
    });

    // Option A: Redirect the SPA with the token in a fragment (no server log)
    if (!string.IsNullOrEmpty(returnUrl))
    {
        var separator = returnUrl.Contains('?') ? "&" : "?";
        var redirectTarget = $"{returnUrl}{separator}token={appToken.Token}&isNewUser={isNewUser}";
        return Redirect(redirectTarget);
    }

    // Option B: Return JSON (useful for Swagger testing or non-SPA clients)
    return Ok(new
    {
        success = true,
        token = appToken.Token,
        expiresAt = appToken.ExpiresAt,
        user = new
        {
            objectId,
            email,
            displayName,
            tenantId
        },
        isNewUser,
        role = defaultRole,
        productId
    });
}

// ═══════════════════════════════════════════════════════════════════════════════
// NOTES
//
// 1. /api/auth/sso/callback must be registered as a Redirect URI in Entra ID.
//    That's the path the OIDC middleware uses (CallbackPath in Program.cs).
//
// 2. The old [HttpGet("sso/callback")] and ExchangeCodeForTokensAsync
//    can be removed entirely. The middleware does the exchange.
//
// 3. The [Authorize(AuthenticationSchemes = "Cookies")] on HandleSsoComplete
//    ensures the user is authenticated via the cookie that the middleware just
//    set. If somehow the cookie is missing, ASP.NET returns 401 automatically.
//
// 4. If _userService doesn't have UserExistsByObjectIdOrEmailAsync, adapt to
//    whatever method you have (the old code used UserExistsAsync).
//
// 5. If _tokenService.GenerateTokenAsync signature differs, adapt accordingly.
//    The key point is: you already have the user's claims — just issue your JWT.
// ═══════════════════════════════════════════════════════════════════════════════
