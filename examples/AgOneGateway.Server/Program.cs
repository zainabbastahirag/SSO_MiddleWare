// ═══════════════════════════════════════════════════════════════════════════════
// EXAMPLE: AG ONE Gateway Server (The Central Product)
// ═══════════════════════════════════════════════════════════════════════════════
// AG ONE is the central gateway that handles Entra ID login and token management.
// The same SSO middleware runs here too — but in "gateway mode" where it doesn't
// call external APIs for token refresh (AG ONE handles its own refresh internally).
// ═══════════════════════════════════════════════════════════════════════════════

using AgOne.Sso.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ─── Step 1: Add AG ONE SSO services in GATEWAY mode ───
builder.Services.AddAgOneSso(builder.Configuration, options =>
{
    options.IsAgOneGateway = true; // ← This is the key setting for AG ONE itself!

    // AG ONE has its own auth pages
    options.AnonymousPaths.Add("/login");
    options.AnonymousPaths.Add("/api/auth/login");
    options.AnonymousPaths.Add("/api/auth/callback");
    options.AnonymousPaths.Add("/api/auth/external/validate"); // Allow other products to call this
    options.AnonymousPaths.Add("/api/productlaunch");
});

// Your existing AG ONE services...
builder.Services.AddControllers();
builder.Services.AddRazorPages();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration["AllowedOrigins"]?.Split(',') ?? Array.Empty<string>())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();

// ─── Step 2: Same middleware — it knows it's on the gateway ───
app.UseAgOneSso();

app.UseAuthorization();

// ─── Step 3: Map SSO endpoints + your existing controllers ───
app.MapAgOneSsoEndpoints();
app.MapControllers();
app.MapRazorPages();
app.MapFallbackToFile("index.html");

app.Run();
