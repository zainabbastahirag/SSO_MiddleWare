using AGOne.Configuration;
using AGOne.Infrastructure.Authentication;
using AGOne.Infrastructure.Data;
using AGOne.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly AGOneDbContext _context;
    private readonly IUserService _userService;
    private readonly ITenantService _tenantService;
    private readonly IRoleService _roleService;
    private readonly IB2CAuthenticationService _b2cAuthService;
    private readonly IEmailService _emailService;
    private readonly IInvitationService _invitationService;
    private readonly IConfiguration _configuration;
    private readonly ITokenStorageService _tokenStorageService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthController> _logger;
    private readonly ServiceBusService _serviceBusService;

    public AuthController(
        AGOneDbContext context,
        IUserService userService,
        ITenantService tenantService,
        IRoleService roleService,
        IB2CAuthenticationService b2cAuthService,
        IEmailService emailService,
        IInvitationService invitationService,
        IConfiguration configuration,
        ITokenStorageService tokenStorageService,
        ILogger<AuthController> logger,
        ITokenService tokenService,
        ServiceBusService serviceBusService)
    {
        _context = context;
        _userService = userService;
        _tenantService = tenantService;
        _roleService = roleService;
        _b2cAuthService = b2cAuthService;
        _emailService = emailService;
        _invitationService = invitationService;
        _configuration = configuration;
        _logger = logger;
        _tokenStorageService = tokenStorageService;
        _tokenService = tokenService;
        _serviceBusService = serviceBusService;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SSO LOGIN — kicks off the Microsoft OIDC flow
    //
    // Browser hits: GET /api/auth/login/microsoft
    //   → middleware generates PKCE, sets cookies, redirects to Microsoft
    //   → user logs in at Microsoft
    //   → Microsoft redirects to /api/auth/sso/callback (middleware intercepts)
    //   → middleware exchanges code for tokens, validates, signs in cookie
    //   → middleware redirects to /api/auth/sso/complete (below)
    // ═══════════════════════════════════════════════════════════════════════════

    [AllowAnonymous]
    [HttpGet("login/microsoft")]
    public IActionResult LoginWithMicrosoft(
        [FromQuery] string? returnUrl = null,
        [FromQuery] string? role = null,
        [FromQuery] string? productId = null)
    {
        var redirectUri = Url.Action(
            nameof(HandleSsoComplete),
            "Auth",
            new { returnUrl, role, productId },
            Request.Scheme);

        _logger.LogInformation("SSO login started. After auth, redirecting to: {RedirectUri}", redirectUri);

        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectUri
        };

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SSO COMPLETE — runs AFTER the middleware has already done everything
    //
    // By the time this action executes:
    //   ✅ The OIDC middleware intercepted /api/auth/sso/callback
    //   ✅ It exchanged the authorization code for tokens (with PKCE)
    //   ✅ It validated the id_token
    //   ✅ It created a ClaimsPrincipal and signed in via Cookie scheme
    //   ✅ HttpContext.User is populated with all claims
    //   ✅ Tokens are available via HttpContext.GetTokenAsync()
    //
    // This action just reads the claims and issues your own application JWT.
    //
    // NOTE: You CANNOT have a controller action at /api/auth/sso/callback
    //       because the OIDC middleware owns that path. That's why this is
    //       at /api/auth/sso/complete instead.
    // ═══════════════════════════════════════════════════════════════════════════

    [Authorize(AuthenticationSchemes = "Cookies")]
    [HttpGet("sso/complete")]
    public async Task<IActionResult> HandleSsoComplete(
        [FromQuery] string? returnUrl = null,
        [FromQuery] string? role = null,
        [FromQuery] string? productId = null)
    {
        // ─── 1. Read claims from the authenticated user ──────────────────
        var email = User.FindFirst("preferred_username")?.Value
                 ?? User.FindFirst("email")?.Value
                 ?? User.FindFirst("emails")?.Value;

        var objectId = User.FindFirst("oid")?.Value
                    ?? User.FindFirst("sub")?.Value;

        var tenantId = User.FindFirst("tid")?.Value;
        var displayName = User.FindFirst("name")?.Value;

        if (string.IsNullOrEmpty(objectId))
        {
            _logger.LogError("SSO complete but no 'oid' or 'sub' claim found");
            return BadRequest(new
            {
                success = false,
                message = "Authentication succeeded but user identity could not be determined."
            });
        }

        _logger.LogInformation(
            "SSO complete — Email: {Email}, ObjectId: {Oid}, TenantId: {Tid}, Name: {Name}",
            email, objectId, tenantId, displayName);

        // ─── 2. Read tokens stored by the middleware (SaveTokens = true) ─
        var accessToken  = await HttpContext.GetTokenAsync("access_token");
        var idToken      = await HttpContext.GetTokenAsync("id_token");
        var refreshToken = await HttpContext.GetTokenAsync("refresh_token");
        var expiresAt    = await HttpContext.GetTokenAsync("expires_at");

        _logger.LogInformation(
            "Tokens received — AccessToken: {HasAccess}, IdToken: {HasId}, RefreshToken: {HasRefresh}, ExpiresAt: {ExpiresAt}",
            !string.IsNullOrEmpty(accessToken),
            !string.IsNullOrEmpty(idToken),
            !string.IsNullOrEmpty(refreshToken),
            expiresAt);

        // ─── 3. Check if user exists or is new ──────────────────────────
        var existingUser = await _userService.GetUserByObjectIdAsync(objectId);
        var isNewUser = existingUser == null;

        if (isNewUser && !string.IsNullOrEmpty(email))
        {
            var userByEmail = await _userService.GetUserByEmailAsync(email);
            if (userByEmail != null)
            {
                isNewUser = false;
                existingUser = userByEmail;
            }
        }

        var defaultRole = role ?? (isNewUser ? "TenantAdmin" : null);

        _logger.LogInformation(
            "User lookup — IsNewUser: {IsNew}, Role: {Role}, ProductId: {ProductId}",
            isNewUser, defaultRole, productId ?? "(none)");

        // ─── 4. Create or update user in your database ──────────────────
        if (isNewUser)
        {
            _logger.LogInformation("Creating new user: {Email}, ObjectId: {Oid}", email, objectId);

            try
            {
                await _userService.CreateUserFromSsoAsync(new CreateSsoUserRequest
                {
                    ObjectId = objectId,
                    Email = email ?? "",
                    DisplayName = displayName ?? "",
                    EntraTenantId = tenantId,
                    Role = defaultRole ?? "TenantAdmin",
                    ProductId = productId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user from SSO");
            }
        }

        // ─── 5. Store tokens if you have a token storage service ────────
        if (!string.IsNullOrEmpty(accessToken))
        {
            try
            {
                await _tokenStorageService.StoreTokensAsync(objectId, new StoredTokens
                {
                    AccessToken = accessToken,
                    IdToken = idToken ?? "",
                    RefreshToken = refreshToken ?? "",
                    ExpiresAt = string.IsNullOrEmpty(expiresAt)
                        ? DateTime.UtcNow.AddHours(1)
                        : DateTime.Parse(expiresAt)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to store tokens — non-fatal");
            }
        }

        // ─── 6. Issue your own application JWT ──────────────────────────
        var appToken = _tokenService.GenerateToken(
            objectId,
            email ?? "",
            displayName ?? "",
            tenantId ?? "",
            defaultRole ?? "User");

        _logger.LogInformation("Application JWT issued for user: {Email}", email);

        // ─── 7. Return response ─────────────────────────────────────────

        // Option A: Redirect the SPA with the token
        if (!string.IsNullOrEmpty(returnUrl))
        {
            var separator = returnUrl.Contains('?') ? "&" : "?";
            var target = $"{returnUrl}{separator}token={appToken.Token}"
                       + $"&isNewUser={isNewUser}"
                       + (!string.IsNullOrEmpty(productId) ? $"&productId={productId}" : "");
            return Redirect(target);
        }

        // Option B: Return JSON
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

    // ═══════════════════════════════════════════════════════════════════════════
    // ... your other existing actions go here (register, logout, etc.)
    // ═══════════════════════════════════════════════════════════════════════════
}
