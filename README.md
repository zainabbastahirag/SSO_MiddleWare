# AG ONE SSO Middleware

**One file. Copy-paste. Done.**

---

## The Full Flow

```
User clicks "Launch AGOneWork" in AG ONE
        │
        ▼
AG ONE ProductLaunchController redirects browser to:
  https://agonework.example.com?token=eyJhbG...
        │
        ▼
Browser hits AGOneWork → Middleware runs:
  1. Sees ?token=xxx in URL
  2. Validates JWT (Entra ID signature + expiry)
  3. Sets HttpOnly cookie (agone_sso_token) on agonework.example.com
  4. Redirects to clean URL: https://agonework.example.com
        │
        ▼
All subsequent requests send cookie automatically
        │
        ├─ Token valid           → Set User, continue
        ├─ Token expiring soon   → Call AG ONE API to refresh, update cookie
        ├─ Token expired         → Call AG ONE API → success? continue : redirect to AG ONE login
        └─ No token / invalid    → API call? 401 : redirect to AG ONE login
```

---

## What to Change in AG ONE

**Only one change:** simplify `ProductLaunchController.Launch()` to just redirect with the token in the query string (instead of setting a cookie that can't cross domains):

```csharp
[HttpGet("launch/{productCode}")]
[AllowAnonymous]
public IActionResult Launch(string productCode, [FromQuery] string token)
{
    var launchUrl = _configuration[$"ProductLaunchUrls:{productCode}"];
    if (string.IsNullOrEmpty(launchUrl))
        return NotFound(new { message = $"Product '{productCode}' not found." });

    if (string.IsNullOrEmpty(token))
        return BadRequest(new { message = "Token is required." });

    // Redirect to product with token in query string.
    // The middleware on Product X picks it up, sets its own cookie, and cleans the URL.
    var url = $"{launchUrl.TrimEnd('/')}?token={Uri.EscapeDataString(token)}";
    return Redirect(url);
}
```

**Why?** Your old code did `Response.Cookies.Append("agone_launch_token", token, ...)` then `Redirect(launchUrl)`. That cookie is set on **AG ONE's domain** — when the browser goes to Product X (a different domain), **Product X can never see that cookie**. Cookies are domain-scoped.

**Everything else in AG ONE stays the same:** Entra ID login, `ValidateAndGetAccessTokenAsync`, `ProductLaunchService`, token storage in DB — no changes.

---

## Setup for Product Apps (3 steps)

### 1. Copy `AgOneSsoMiddleware.cs` into your Server project + install NuGet

```bash
dotnet add package Microsoft.IdentityModel.Protocols.OpenIdConnect
```

### 2. Program.cs — add 3 lines

```csharp
using AgOne.Sso;

builder.Services.AddAgOneSso(builder.Configuration);   // ← LINE 1

app.UseRouting();
app.UseCors();
app.UseAgOneSso();              // ← LINE 2 (after routing, before auth)
app.UseAuthorization();

app.MapControllers();
app.MapAgOneSsoEndpoints();     // ← LINE 3
app.MapFallbackToFile("index.html");
```

### 3. appsettings.json

```json
{
  "AgOneSso": {
    "AgOneBaseUrl": "https://agone.yourdomain.com",
    "AgOneLoginUrl": "https://agone.yourdomain.com",
    "TenantId": "YOUR-ENTRA-TENANT-ID",
    "ClientId": "YOUR-ENTRA-CLIENT-ID",
    "ValidAudience": "api://YOUR-ENTRA-CLIENT-ID",
    "IsAgOneGateway": false
  }
}
```

---

## Setup for AG ONE Itself (optional)

Same file, same 3 lines. Config:

```json
{
  "AgOneSso": {
    "AgOneBaseUrl": "https://agone.yourdomain.com",
    "AgOneLoginUrl": "https://agone.yourdomain.com/login",
    "TenantId": "YOUR-ENTRA-TENANT-ID",
    "ClientId": "YOUR-ENTRA-CLIENT-ID",
    "ValidAudience": "api://YOUR-ENTRA-CLIENT-ID",
    "IsAgOneGateway": true,
    "AnonymousPaths": [
      "/login",
      "/api/auth/login",
      "/api/auth/callback",
      "/api/auth/external/validate",
      "/api/productlaunch"
    ]
  }
}
```

---

## Blazor WASM Client (all products)

Add this class to your WASM project:

```csharp
// CookieHandler.cs
using Microsoft.AspNetCore.Components.WebAssembly.Http;

public class CookieHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
```

Wire it up in WASM `Program.cs`:

```csharp
builder.Services.AddTransient<CookieHandler>();
builder.Services.AddHttpClient("Backend",
    c => c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<CookieHandler>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Backend"));
```
