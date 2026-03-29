using AgOne.Sso;
using AGOne.Configuration;
using AGOne.Infrastructure;
using AGOne.Infrastructure.Authentication;
using AGOne.Infrastructure.Data;
using AGOne.Infrastructure.Services;
using AGOne.UI.Services;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Models.ExternalConnectors;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════════════════════
// CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

var jwtSection = builder.Configuration.GetSection("Jwt");
var agOneSso = builder.Configuration.GetSection("AgOneSso");

var ssoInstance    = agOneSso["Instance"]?.TrimEnd('/') ?? "";
var ssoTenantId   = agOneSso["TenantId"] ?? "";
var ssoClientId   = agOneSso["ClientId"] ?? "";
var ssoClientSecret = agOneSso["ClientSecret"] ?? "";
var ssoCallbackPath = agOneSso["CallbackPath"] ?? "/api/auth/sso/callback";
var ssoScopes     = agOneSso.GetSection("Scopes").Get<string[]>() ?? new[] { "openid", "profile", "email", "offline_access" };

// ═══════════════════════════════════════════════════════════════════════════════
// INFRASTRUCTURE SERVICES
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddInfrastructure(builder.Configuration);

// ═══════════════════════════════════════════════════════════════════════════════
// CONTROLLERS & RAZOR PAGES
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRazorPages();

// ═══════════════════════════════════════════════════════════════════════════════
// SWAGGER
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AGOne Central Authentication & Marketplace API",
        Version = "v1",
        Description = "Central authentication service for AGOne products with SSO support via Microsoft Entra ID",
        Contact = new OpenApiContact
        {
            Name = "AGOne Support",
            Email = "support@agone.com"
        }
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ═══════════════════════════════════════════════════════════════════════════════
// APPLICATION SERVICES
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITenantMasterDataService, TenantMasterDataService>();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddHttpClient<IB2CAuthenticationService, B2CAuthenticationService>();
builder.Services.AddHttpClient("EntraId");

// ═══════════════════════════════════════════════════════════════════════════════
// AZURE SERVICE BUS (global-logout topic)
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["AzureServiceBus:ConnectionString"]!;
    var topicName = config["AzureServiceBus:TopicName"]!;
    var subscriptionName = config["AzureServiceBus:SubscriptionName"]!;
    return new ServiceBusService(connectionString, topicName, subscriptionName, sp);
});

// ═══════════════════════════════════════════════════════════════════════════════
// AZURE SERVICE BUS (voicecommand-topic)
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["AzureServiceBus:ConnectionString"]!;
    var topicName = config["AzureServiceBus:VoiceCommandTopic"]!;
    var subscriptionName = config["AzureServiceBus:SubscriptionName"]!;
    return new ServiceBusService(connectionString, topicName, subscriptionName, sp);
});

builder.Services.AddHostedService<ServiceBusWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<EmployeeMappingProfile>());
builder.Services.AddScoped<IEmployeeMappingService, EmployeeMappingService>();
builder.Services.AddScoped<IInvitationApiService, InvitationApiService>();

// ═══════════════════════════════════════════════════════════════════════════════
// AUTHENTICATION - Cookie + OpenID Connect + JWT Bearer (multi-scheme)
// ═══════════════════════════════════════════════════════════════════════════════

var isLocalDev = builder.Configuration.GetValue<bool>("IsDevelopment");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    // FIX 1: SameSite.None requires Secure. On local HTTP dev use Lax instead.
    if (isLocalDev)
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
    else
    {
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
    options.Cookie.HttpOnly = true;

    // FIX 2: Forward to JWT Bearer when the request carries a Bearer token.
    // Without this, API calls with "Authorization: Bearer <token>" are handled
    // by the Cookie scheme, which fails (no cookie) and then challenges via
    // OpenIdConnect — resulting in a 302 redirect instead of a 401.
    options.ForwardDefaultSelector = context =>
    {
        string? authorization = context.Request.Headers.Authorization;
        if (!string.IsNullOrEmpty(authorization) &&
            authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }
        return null;
    };

    // FIX 3: For API-style requests, return 401/403 instead of redirecting to
    // the OIDC login page. This prevents browsers and API clients from receiving
    // a confusing 302 when they hit a protected endpoint without credentials.
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }
    };
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.Authority = $"{ssoInstance}/{ssoTenantId}/v2.0";
    options.ClientId = ssoClientId;
    options.ClientSecret = ssoClientSecret;
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.ResponseMode = OpenIdConnectResponseMode.Query;
    options.SaveTokens = true;

    // Dedicated OIDC middleware callback — must NOT collide with
    // AuthController's [HttpGet("sso/callback")] at /api/auth/sso/callback.
    // Register /signin-oidc as a redirect URI in your Entra ID app registration.
    options.CallbackPath = "/signin-oidc";

    if (isLocalDev)
    {
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
    else
    {
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.SameSite = SameSiteMode.None;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
    }

    options.MetadataAddress =
        $"{ssoInstance}/{ssoTenantId}/v2.0/.well-known/openid-configuration";

    options.Scope.Clear();
    foreach (var scope in ssoScopes)
    {
        options.Scope.Add(scope);
    }

    options.Events = new OpenIdConnectEvents
    {
        OnRedirectToIdentityProvider = ctx =>
        {
            Console.WriteLine($"Redirecting to IDP: {ctx.ProtocolMessage.RedirectUri}");
            return Task.CompletedTask;
        },
        OnRemoteFailure = ctx =>
        {
            Console.WriteLine($"OIDC remote failure: {ctx.Failure?.Message}");
            ctx.HandleResponse();
            ctx.Response.StatusCode = 400;
            return ctx.Response.WriteAsync($"SSO login failed: {ctx.Failure?.Message}");
        }
    };

    options.SkipUnrecognizedRequests = true;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var jwt = builder.Configuration.GetSection("Jwt");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!)),
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();

            if (context.Exception is SecurityTokenExpiredException)
            {
                context.Response.Headers.Append("Token-Expired", "true");
                logger.LogWarning(
                    "JWT token expired for request to {Path}",
                    context.Request.Path);
            }
            else
            {
                logger.LogWarning(
                    context.Exception,
                    "JWT authentication failed for {Path}",
                    context.Request.Path);
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();

            var userId =
                context.Principal?.FindFirst("oid")?.Value ??
                context.Principal?.FindFirst("sub")?.Value;

            logger.LogInformation("JWT token validated for user: {UserId}", userId);

            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            // For JWT, always return 401 — never redirect
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.Append("WWW-Authenticate", "Bearer");
            return Task.CompletedTask;
        }
    };
});

// ═══════════════════════════════════════════════════════════════════════════════
// AUTHORIZATION POLICIES
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddAuthorization(options =>
{
    // DefaultPolicy is used when [Authorize] is applied without a named policy.
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // FallbackPolicy = null means endpoints WITHOUT any [Authorize] attribute
    // are accessible anonymously. [AllowAnonymous] also overrides [Authorize].
    options.FallbackPolicy = null;

    options.AddPolicy("ApiUser", policy =>
        policy.RequireAuthenticatedUser());

    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireRole("PlatformAdmin"));

    options.AddPolicy("TenantAdmin", policy =>
        policy.RequireRole("TenantAdmin", "PlatformAdmin"));

    options.AddPolicy("ProductAdmin", policy =>
        policy.RequireRole("ProductAdmin", "TenantAdmin", "PlatformAdmin"));
});

builder.Services.AddAgOneSso(builder.Configuration);

// ═══════════════════════════════════════════════════════════════════════════════
// CORS
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("ExternalApps", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// ═══════════════════════════════════════════════════════════════════════════════
// SESSION & CACHING
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ═══════════════════════════════════════════════════════════════════════════════
// BUILD APPLICATION
// ═══════════════════════════════════════════════════════════════════════════════

var app = builder.Build();

// ═══════════════════════════════════════════════════════════════════════════════
// DATABASE INITIALIZATION
// ═══════════════════════════════════════════════════════════════════════════════

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("AutoMigrate"))
{
    try
    {
        await app.Services.InitializeDatabaseAsync();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Database migration failed. Some features may not work until the database is configured.");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// MIDDLEWARE PIPELINE (order preserved)
// ═══════════════════════════════════════════════════════════════════════════════

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AGOne API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "AGOne API Documentation";
});

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors("AllowAll");

app.UseAgOneSso();

app.UseAuthentication();
app.UseSession();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.MapAgOneSsoEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

// ═══════════════════════════════════════════════════════════════════════════════
// HELPERS
// ═══════════════════════════════════════════════════════════════════════════════

static bool IsApiRequest(HttpRequest request)
{
    return request.Path.StartsWithSegments("/api") ||
           request.Headers.Accept.Any(h =>
               h != null && h.Contains("application/json", StringComparison.OrdinalIgnoreCase)) ||
           request.Headers.ContainsKey("X-Requested-With");
}

// ═══════════════════════════════════════════════════════════════════════════════
// TENANT PROVIDER
// ═══════════════════════════════════════════════════════════════════════════════

public class TenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            var tenantHeader = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            if (Guid.TryParse(tenantHeader, out var tenantId))
                return tenantId;

            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(claim, out var claimTenantId))
                return claimTenantId;

            var entraTid = _httpContextAccessor.HttpContext?.User?.FindFirst("tid")?.Value;
            if (Guid.TryParse(entraTid, out var entraTenantId))
                return entraTenantId;

            return null;
        }
    }
}
