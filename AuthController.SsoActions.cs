using AGOne.Configuration;
using AGOne.Infrastructure.Authentication;
using AGOne.Infrastructure.Data;
using AGOne.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
            new { role, productId },
            Request.Scheme);

        _logger.LogInformation("SSO login started. After auth, redirecting to: {RedirectUri}", redirectUri);

        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectUri
        };

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SSO COMPLETE — all your original HandleSsoCallback logic lives here
    //
    // The ONLY difference from the old callback:
    //   OLD: got user info from ExchangeCodeForTokensAsync(code, state)
    //   NEW: gets user info from HttpContext.User claims (middleware already
    //        exchanged the code and validated the tokens)
    // ═══════════════════════════════════════════════════════════════════════════

    [Authorize(AuthenticationSchemes = "Cookies")]
    [HttpGet("sso/complete")]
    public async Task<IActionResult> HandleSsoComplete(
        [FromQuery] string? role = null,
        [FromQuery] string? productId = null)
    {
        // ─── 1. Extract user info from claims (replaces ExchangeCodeForTokensAsync) ─

        var ssoEmail = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst("email")?.Value
                    ?? User.FindFirst("emails")?.Value;

        var ssoObjectId = User.FindFirst("oid")?.Value
                       ?? User.FindFirst("sub")?.Value;

        var ssoTenantId = User.FindFirst("tid")?.Value;

        var ssoFirstName = User.FindFirst("given_name")?.Value ?? "";
        var ssoLastName = User.FindFirst("family_name")?.Value ?? "";
        var ssoDisplayName = User.FindFirst("name")?.Value ?? "";
        var ssoJobTitle = User.FindFirst("jobTitle")?.Value ?? "";

        if (string.IsNullOrEmpty(ssoObjectId) || string.IsNullOrEmpty(ssoEmail))
        {
            _logger.LogError("SSO complete but missing claims — oid: {Oid}, email: {Email}", ssoObjectId, ssoEmail);
            return BadRequest(new AuthResponse { Success = false, Message = "Authentication failed — missing user claims" });
        }

        // Fill in first/last name from display name if claims not present
        if (string.IsNullOrEmpty(ssoFirstName) && !string.IsNullOrEmpty(ssoDisplayName))
        {
            var parts = ssoDisplayName.Split(' ', 2);
            ssoFirstName = parts[0];
            ssoLastName = parts.Length > 1 ? parts[1] : "";
        }

        _logger.LogInformation("SSO complete for user {Email}", ssoEmail);

        // Read tokens stored by the middleware
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        var refreshToken = await HttpContext.GetTokenAsync("refresh_token");
        var expiresAtStr = await HttpContext.GetTokenAsync("expires_at");
        var tokenExpiresAt = !string.IsNullOrEmpty(expiresAtStr)
            ? DateTime.Parse(expiresAtStr).ToUniversalTime()
            : DateTime.UtcNow.AddHours(1);

        // ─── 2. Parse productId ─────────────────────────────────────────────

        Guid? parsedProductId = null;
        if (!string.IsNullOrEmpty(productId) && Guid.TryParse(productId, out var pid))
        {
            parsedProductId = pid;
            _logger.LogInformation("ProductId from SSO state: {ProductId}", parsedProductId);
        }

        // ─── 3. Find or create user (your original logic, unchanged) ────────

        var defaultRole = role;

        var user = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == ssoEmail);

        if (user == null)
        {
            // New SSO user — determine tenant
            Tenant? tenant = null;

            // Try to get tenant from Entra tenant ID
            if (!string.IsNullOrEmpty(ssoTenantId) && Guid.TryParse(ssoTenantId, out var tenantIdFromState))
            {
                tenant = await _tenantService.GetByIdAsync(tenantIdFromState);
            }

            if (tenant == null && !string.IsNullOrEmpty(ssoEmail))
            {
                var companyName = ExtractCompanyFromEmail(ssoEmail);
                tenant = await _tenantService.GetByNameAsync(companyName);
                if (tenant != null)
                {
                    defaultRole = ProductRoles.Employee;
                }
            }

            // Check for pending invitation
            var invitation = await _context.UserInvitations
                .Include(i => i.Tenant)
                .FirstOrDefaultAsync(i => i.Email == ssoEmail && i.Status == InvitationStatus.Pending);

            if (invitation != null)
            {
                tenant = invitation.Tenant;

                await _invitationService.AcceptInvitationAsync(new AcceptInvitationRequest
                {
                    Token = invitation.Token,
                    FirstName = ssoFirstName,
                    LastName = ssoLastName,
                    SsoProvider = "Microsoft Entra ID",
                    SsoSubjectId = ssoObjectId,
                    SsoEmail = ssoEmail
                });

                user = await _context.Users
                    .Include(u => u.Tenant)
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Email == ssoEmail);
            }
            else if (tenant == null)
            {
                // No tenant found — self-registration
                var companyName = ExtractCompanyFromEmail(ssoEmail);
                var identifier = GenerateUniqueIdentifier(companyName);

                tenant = new Tenant
                {
                    Name = companyName,
                    Identifier = identifier,
                    ContactEmail = ssoEmail,
                    Status = TenantStatus.Trial
                };
                tenant = await _tenantService.CreateAsync(tenant);

                await CreateDefaultRolesAsync(tenant.Id);
            }

            // Create subscription if product parameter provided
            if (parsedProductId.HasValue && tenant != null)
            {
                await CreateSubscriptionForTenantAsync(tenant.Id, parsedProductId.Value);
            }

            if (user == null)
            {
                user = new User
                {
                    TenantId = tenant!.Id,
                    Email = ssoEmail,
                    FirstName = ssoFirstName,
                    LastName = ssoLastName,
                    JobTitle = ssoJobTitle,
                    SSOProvider = "Microsoft Entra ID",
                    SSOSubjectId = ssoObjectId,
                    IsActive = true,
                    EmailConfirmed = true
                };
                user = await _userService.CreateAsync(user);

                var assignRole = defaultRole ?? TenantRoles.Admin;
                var roleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Name == assignRole);
                roleEntity ??= await _context.Roles.FirstOrDefaultAsync(r => r.Name == ProductRoles.Employee);

                if (roleEntity != null)
                {
                    await _userService.AssignRoleAsync(user.Id, roleEntity.Id);
                }

                user = await _userService.GetByIdAsync(user.Id);
            }
        }
        else
        {
            // Existing user — update SSO info if needed
            if (string.IsNullOrEmpty(user.SSOSubjectId))
            {
                user.SSOProvider = "Microsoft Entra ID";
                user.SSOSubjectId = ssoObjectId;
            }
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            if (parsedProductId.HasValue)
            {
                await CreateSubscriptionForTenantAsync(user.TenantId, parsedProductId.Value);
            }
        }

        // ─── 4. Log login attempt ───────────────────────────────────────────

        await LogLoginAttemptAsync(
            user!.Id,
            user.TenantId,
            LoginType.SSO,
            true,
            ssoProvider: "Microsoft Entra ID",
            ssoSubjectId: ssoObjectId);

        // ─── 5. Generate JWT and build user DTO ─────────────────────────────

        var token = _tokenService.GenerateJwtToken(user);
        var userDto = await BuildUserDtoAsync(user);

        string baseUrl = _configuration.GetValue<string>("Application:BaseUrl")!;

        var userJson = JsonSerializer.Serialize(userDto);
        var userEncoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userJson));

        // ─── 6. Save token ──────────────────────────────────────────────────

        try
        {
            var saveToken = new UserToken
            {
                UserId = user.Id.ToString(),
                TenantId = user.TenantId.ToString(),
                AccessToken = token ?? string.Empty,
                RefreshToken = refreshToken,
                IdToken = "",
                AccessTokenExpiresUtc = tokenExpiresAt,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await _tokenStorageService.SaveOrUpdateTokenAsync(saveToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HandleSsoComplete => Failed to save token");
        }

        // ─── 7. Redirect to SPA ────────────────────────────────────────────

        var redirectUrl = $"{baseUrl}/sso-login?token={token}&user={userEncoded}";
        return Redirect(redirectUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ... your other existing actions go here (register, logout, etc.)
    //
    // Also keep your existing private helper methods:
    //   - ExtractCompanyFromEmail(string email)
    //   - GenerateUniqueIdentifier(string companyName)
    //   - CreateDefaultRolesAsync(Guid tenantId)
    //   - CreateSubscriptionForTenantAsync(Guid tenantId, Guid productId)
    //   - LogLoginAttemptAsync(...)
    //   - BuildUserDtoAsync(User user)
    // ═══════════════════════════════════════════════════════════════════════════
}
