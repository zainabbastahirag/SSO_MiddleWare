# AG ONE SSO Middleware

**One file. Copy-paste. Done.**

A single self-contained middleware file (`AgOneSsoMiddleware.cs`) that handles SSO authentication across all AG ONE products (Blazor WebAssembly + .NET 8 backend).

---

## How It Works

```
User clicks "Launch Product X" in AG ONE
        │
        ▼
AG ONE sets cookie (agone_launch_token) → redirects to Product X
        │
        ▼
Product X: Middleware picks up token from cookie
        │
        ├─ Valid token         → Set User, set session cookie, continue
        ├─ Expiring soon       → Refresh via AG ONE API, update cookie, continue
        ├─ Expired             → Refresh via AG ONE API → success? continue : redirect to login
        ├─ Invalid / no token  → API call? 401 JSON : redirect to AG ONE login
        └─ Static file (.js/.css/.wasm) → skip, always serve
```

---

## Setup for Product Apps (AGOneWork, AGOneLearn, etc.)

### Step 1 — Copy the file

Copy `AgOneSsoMiddleware.cs` into your Server project.

### Step 2 — Install NuGet

```bash
dotnet add package Microsoft.IdentityModel.Protocols.OpenIdConnect
```

### Step 3 — Program.cs (add 3 lines)

```csharp
using AgOne.Sso;               // ← add this using

// ... your existing builder setup ...

builder.Services.AddAgOneSso(builder.Configuration);   // ← LINE 1

// ... your existing app setup ...

app.UseRouting();
app.UseCors();
app.UseAgOneSso();              // ← LINE 2 (after routing, before auth)
app.UseAuthorization();

app.MapControllers();
app.MapAgOneSsoEndpoints();     // ← LINE 3 (maps /api/auth/sso-user-info & sso-logout)
app.MapFallbackToFile("index.html");
```

### Step 4 — appsettings.json

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

**That's it. Product app is SSO-enabled.**

---

## Setup for AG ONE Gateway (the central product)

Same file, same 3 lines. Only the config changes:

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

Key difference: `IsAgOneGateway: true` means the middleware won't call any external refresh API — AG ONE handles its own token refresh internally.

**Nothing changes in your existing AG ONE Entra ID login, token storage, or Product Launcher code.** The middleware just sits in front and protects routes.

---

## Blazor WASM Client Setup (all products)

Add this small class to your **WASM Client** project:

```csharp
// CookieHandler.cs (in your WASM Client project)
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

Then in WASM `Program.cs`:

```csharp
builder.Services.AddTransient<CookieHandler>();
builder.Services.AddHttpClient("Backend",
    c => c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<CookieHandler>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Backend"));
```

This ensures cookies are sent with every HTTP request from the browser.

---

## What AG ONE Needs (you already have this)

The middleware calls your existing AG ONE endpoint to refresh expired tokens:

```
POST /api/auth/external/validate
Body: { "token": "expired-access-token" }
Response: { "token": "new-valid-access-token" }
```

This is your existing `ValidateAndGetAccessTokenAsync` method — **no changes needed**.

Your existing Product Launcher that sets `agone_launch_token` cookie and redirects — **no changes needed**.

---

## All Scenarios Handled

| Scenario | What Happens |
|----------|-------------|
| Launch from AG ONE Product Launcher | Token from launch cookie → validated → session cookie set |
| Subsequent page loads | Token from session cookie → validated |
| Blazor WASM API calls | Cookie sent automatically (via CookieHandler) |
| Token valid & fresh | User identity set, request continues |
| Token expiring within 5 min | Proactive refresh via AG ONE API |
| Token expired | Refresh via AG ONE → if fails → redirect to login |
| No token / direct URL access | Redirect to AG ONE login (with returnUrl) |
| API call without token | 401 JSON response |
| Static files (.js, .css, .wasm, .dll) | Always served, no auth check |
| Blazor framework / SignalR | Always served, no auth check |
| Token in query string (?token=xxx) | Accepted, cookie set, redirected to clean URL |
| Invalid / tampered JWT | Rejected, redirect to login |
| AG ONE gateway mode | Same middleware, no external refresh calls |

---

## Configuration Reference

| Setting | Default | Description |
|---------|---------|-------------|
| `AgOneBaseUrl` | *(required)* | AG ONE API base URL |
| `AgOneLoginUrl` | `AgOneBaseUrl` | Login page for redirects |
| `TenantId` | *(required)* | Entra ID Tenant ID |
| `ClientId` | *(required)* | Entra ID Client ID |
| `ValidAudience` | `ClientId` | Expected JWT audience |
| `IsAgOneGateway` | `false` | Set `true` for AG ONE itself |
| `AnonymousPaths` | `[]` | Paths that skip auth |
| `SessionCookieName` | `agone_sso_token` | Session cookie name |
| `LaunchCookieName` | `agone_launch_token` | Launch cookie name |
| `SessionCookieLifetime` | `60 min` | How long session cookie lasts |
| `RefreshBufferMinutes` | `5` | Minutes before expiry to refresh |
| `CookieDomain` | *(auto)* | Shared domain for cookies |
| `CookieSameSite` | `None` | SameSite cookie policy |

---

## API Endpoints (auto-mapped)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/auth/sso-user-info` | GET | Returns current user claims (for Blazor WASM) |
| `/api/auth/sso-logout` | POST | Clears SSO cookies |
