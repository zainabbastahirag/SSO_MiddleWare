// ═══════════════════════════════════════════════════════════════════════════════
// EXAMPLE: Product App Server (e.g., AGOneWork, AGOneLearn)
// ═══════════════════════════════════════════════════════════════════════════════
// This is the .NET backend for a product that receives SSO tokens from AG ONE.
// Copy this pattern into your product's Program.cs.
// ═══════════════════════════════════════════════════════════════════════════════

using AgOne.Sso.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ─── Step 1: Add AG ONE SSO services ───
builder.Services.AddAgOneSso(builder.Configuration, options =>
{
    // Optional: Add any product-specific anonymous paths
    options.AnonymousPaths.Add("/api/health");
    options.AnonymousPaths.Add("/api/public");
});

// Add your other services
builder.Services.AddControllers();
builder.Services.AddRazorPages();

// CORS — needed if Blazor WASM is on a different origin
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration["AllowedOrigins"]?.Split(',') ?? Array.Empty<string>())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // ← Required for cookies
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

// ─── Step 2: Add the AG ONE SSO middleware ───
// MUST be after UseRouting() and UseCors() but BEFORE UseAuthorization()
app.UseAgOneSso();

app.UseAuthorization();

// ─── Step 3: Map the SSO endpoints (user-info, logout, health) ───
app.MapAgOneSsoEndpoints();

app.MapControllers();
app.MapRazorPages();
app.MapFallbackToFile("index.html");

app.Run();
