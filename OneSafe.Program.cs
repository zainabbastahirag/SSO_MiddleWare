using AgOne.Sso;
using AGOneSafe.Core.Application.DTOs;
using AGOneSafe.Core.Application.Interfaces;
using AGOneSafe.Core.Application.Services;
using AGOneSafe.Core.Domain.Interfaces;
using AGOneSafe.Helper;
using AGOneSafe.Infrastructure;
using AGOneSafe.Infrastructure.Data;
using AGOneSafe.Infrastructure.Middleware;
using AGOneSafe.Infrastructure.Repositories;
using AGOneSafe.Infrastructure.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using MudBlazor.Services;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ---------------------------
// Add Services
// ---------------------------
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.HideTransitionDuration = 500;
    config.SnackbarConfiguration.ShowTransitionDuration = 500;
    config.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Filled;
});

// Radzen services
builder.Services.AddScoped<Radzen.DialogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TooltipService>();
builder.Services.AddScoped<ContextMenuService>();

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// AG ONE SSO — handles JWT validation from cookie, sets ctx.User
builder.Services.AddAgOneSso(builder.Configuration);

builder.Services.AddAuthorization();

// DbContext
builder.Services.AddDbContext<SafeDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions => sqlServerOptions
            .EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null)
            .CommandTimeout(60)
    )
);

// ---------------------------
// Register Repositories
// ---------------------------
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IDocumentDetailRepository, DocumentDetailRepository>();
builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPolicyDisabledRepository, PolicyDisabledRepository>();
builder.Services.AddScoped<ICheckItemRepository, CheckItemRepository>();
builder.Services.AddScoped<IControlRepository, ControlRepository>();
builder.Services.AddScoped<IMasterDropdownRepository, MasterDropdownRepository>();
builder.Services.AddScoped<IPolicyAckRequestRepository, PolicyAckRequestRepository>();
builder.Services.AddScoped<IPolicyAckItemRepository, PolicyAckItemRepository>();
builder.Services.AddScoped<IDocumentHistoryRepository, DocumentHistoryRepository>();
builder.Services.AddScoped<ISecurityRolesRepository, SecurityRolesRepository>();
builder.Services.AddScoped<ISecurityRolesFNRepository, SecurityRolesFNRepository>();
builder.Services.AddScoped<IPolicyAckDueDateRepository, PolicyAckDueDateRepository>();
builder.Services.AddScoped<IPolicyControlRepository, PolicyControlRepository>();
builder.Services.AddScoped<IFrameworkRepository, FrameworkRepository>();
builder.Services.AddScoped<ICriteriaRepository, CriteriaRepository>();
builder.Services.AddScoped<IControlFrameworkRepository, ControlFrameworkRepository>();
builder.Services.AddScoped<IControlFrameworkCriteriaRepository, ControlFrameworkCriteriaRepository>();
builder.Services.AddScoped<IControlsListRepository, ControlsListRepository>();
builder.Services.AddScoped<IFrameworkControlRepository, FrameworkControlRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IControlCheckRepository, ControlCheckRepository>();
builder.Services.AddScoped<IPolicyTemplatesRepository, PolicyTemplatesRepository>();
builder.Services.AddScoped<IPolicyTemplateHistoryRepository, PolicyTemplateHistoryRepository>();
builder.Services.AddScoped<IPolicyTemplateFrameworkRepository, PolicyTemplateFrameworkRepository>();

// Unit of Work
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Infrastructure Services
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<MyIcons>();

// Application Services
builder.Services.AddScoped<IPolicyService, PolicyService>();
builder.Services.AddScoped<IAcknowledgementStatusService, AcknowledgementStatusService>();
builder.Services.AddScoped<IAcknowledgementRequestService, AcknowledgementRequestService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IMessageBoxService, MessageBoxService>();
builder.Services.AddScoped<ICheckItemService, CheckItemService>();
builder.Services.AddScoped<IControlService, ControlService>();
builder.Services.AddScoped<IControlFrameworkService, ControlFrameworkService>();
builder.Services.AddScoped<IMasterDropdownService, MasterDropdownService>();
builder.Services.AddScoped<IClipboardService, ClipboardService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IPolicyDisabledService, PolicyDisabledService>();
builder.Services.AddScoped<IPolicyControlService, PolicyControlService>();
builder.Services.AddScoped<ISecurityRoleService, SecurityRoleService>();
builder.Services.AddScoped<IGlobalSearchService, GlobalSearchService>();
builder.Services.AddScoped<IFrameworkService, FrameworkService>();
builder.Services.AddScoped<ICriteriaService, CriteriaService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IPdfGenerationService, PdfGenerationService>();
builder.Services.AddScoped<IFrameworkControlService, FrameworkControlService>();
builder.Services.AddScoped<IControlFrameworkCriteriaService, ControlFrameworkCriteriaService>();
builder.Services.AddScoped<IControlCheckService, ControlCheckService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IPolicyTemplatesService, PolicyTemplatesService>();
builder.Services.AddScoped<ITokenService, TokenService>();

// ---------------------------
// Build App
// ---------------------------
var app = builder.Build();

// Initialize database
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("AutoMigrate"))
{
    try
    {
        await app.Services.InitializeDatabaseAsync();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Database migration failed.");
    }
}

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

    var csp =
        "default-src 'self'; " +
        "script-src 'self' https://code.jquery.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self' ws://localhost:51310 wss://localhost:5001 wss://localhost:44359 wss://*.azurewebsites.net; " +
        "base-uri 'self'; " +
        "form-action 'self';";

#if DEBUG
    csp = csp.Replace(
        "connect-src 'self' ws://localhost:51310 wss://localhost:5001 wss://localhost:44359;",
        "connect-src 'self' http://localhost:51310 ws://localhost:51310 wss://localhost:5001 wss://localhost:44359;"
    );
#endif

    context.Response.Headers["Content-Security-Policy"] = csp;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";

    await next();
});

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto
});

// ---------------------------
// Middleware Pipeline (ORDER MATTERS!)
// ---------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAgOneSso();           // Sets ctx.User from cookie
app.UseAuthorization();

// All Map* calls AFTER Use* middleware
app.MapControllers();
app.MapAgOneSsoEndpoints();
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
