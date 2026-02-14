# AG ONE SSO Middleware

A plug-and-play SSO middleware for the AG ONE product ecosystem. Drop it into any .NET 8 Blazor WebAssembly product and it handles authentication automatically — token validation, refresh, cookie management, and redirect flows.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         AG ONE (Central Gateway)                     │
│                                                                      │
│  ┌──────────────┐   ┌──────────────────┐   ┌─────────────────────┐  │
│  │  Entra ID    │──▶│  Token Storage    │──▶│  Product Launcher   │  │
│  │  Login/SSO   │   │  (DB: UserTokens) │   │  (Sets cookie +    │  │
│  └──────────────┘   └──────────────────┘   │   redirects)        │  │
│                              │              └─────────┬───────────┘  │
│                              │                        │              │
│  ┌──────────────────────┐    │  /api/auth/external/   │              │
│  │  SSO Middleware       │    │  validate              │              │
│  │  (IsAgOneGateway=true)│◀──┘  (refresh endpoint)    │              │
│  └──────────────────────┘                             │              │
└───────────────────────────────────────────────────────┼──────────────┘
                                                        │
                    ┌───────────────────────────────────┘
                    │  Redirect with launch cookie
                    ▼
┌──────────────────────────────────────┐    ┌──────────────────────────────────┐
│          Product A (e.g. Work)        │    │          Product B (e.g. Learn)   │
│                                       │    │                                   │
│  ┌──────────────────────────────┐    │    │  ┌──────────────────────────────┐ │
│  │  SSO Middleware               │    │    │  │  SSO Middleware               │ │
│  │  (Same middleware!)           │    │    │  │  (Same middleware!)           │ │
│  │                               │    │    │  │                               │ │
│  │  1. Extract token (cookie/    │    │    │  │  1. Extract token             │ │
│  │     header/query)             │    │    │  │  2. Validate JWT              │ │
│  │  2. Validate JWT locally      │    │    │  │  3. Refresh via AG ONE API    │ │
│  │  3. If expired → call AG ONE  │    │    │  │  4. Set ClaimsPrincipal       │ │
│  │     to refresh                │    │    │  │  5. Set session cookie        │ │
│  │  4. Set ClaimsPrincipal       │    │    │  │                               │ │
│  │  5. Set session cookie        │    │    │  └──────────────────────────────┘ │
│  └──────────────────────────────┘    │    │                                   │
│                                       │    │  ┌──────────────────────────────┐ │
│  ┌──────────────────────────────┐    │    │  │  Blazor WASM Client          │ │
│  │  Blazor WASM Client          │    │    │  │  (AuthStateProvider + Cookie  │ │
│  │  (AuthStateProvider + Cookie  │    │    │  │   Handler)                   │ │
│  │   Handler)                    │    │    │  └──────────────────────────────┘ │
│  └──────────────────────────────┘    │    │                                   │
└──────────────────────────────────────┘    └──────────────────────────────────┘
```

## Token Flow

```
User clicks "Launch Product X" in AG ONE
    │
    ▼
AG ONE Product Launcher validates token, sets HttpOnly cookie, redirects to Product X
    │
    ▼
Product X receives request with launch cookie (agone_launch_token)
    │
    ▼
SSO Middleware extracts token from launch cookie
    │
    ▼
JWT validated locally using Entra ID signing keys
    │
    ├── Valid & not expiring ──────▶ Set ClaimsPrincipal, set session cookie, continue
    │
    ├── Valid but expiring soon ──▶ Proactively call AG ONE refresh API
    │                                  │
    │                                  ├── Refresh OK ──▶ Update cookie, continue
    │                                  └── Refresh fail ▶ Continue with current token
    │
    ├── Expired ──────────────────▶ Call AG ONE refresh API
    │                                  │
    │                                  ├── Refresh OK ──▶ Update cookie, continue
    │                                  └── Refresh fail ▶ Redirect to AG ONE login
    │
    └── Invalid/No token ─────────▶ API request? Return 401 JSON
                                    Page request? Redirect to AG ONE login
```

## Quick Start (3 Steps)

### Step 1: Add the Project Reference

Reference the middleware class library from your product's `.csproj`:

```xml
<ProjectReference Include="path/to/AgOne.Sso.Middleware/AgOne.Sso.Middleware.csproj" />
```

Or if you publish it as a NuGet package:

```xml
<PackageReference Include="AgOne.Sso.Middleware" Version="1.0.0" />
```

### Step 2: Configure Your Server (Program.cs)

```csharp
using AgOne.Sso.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add AG ONE SSO services
builder.Services.AddAgOneSso(builder.Configuration);

// ... your other services ...

var app = builder.Build();

app.UseRouting();
app.UseCors();

// Add the SSO middleware (after UseRouting, before UseAuthorization)
app.UseAgOneSso();

app.UseAuthorization();

// Map SSO endpoints (user-info, logout, health)
app.MapAgOneSsoEndpoints();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
```

### Step 3: Add Configuration (appsettings.json)

```json
{
  "AgOneSso": {
    "AgOneBaseUrl": "https://agone.yourdomain.com",
    "AgOneLoginUrl": "https://agone.yourdomain.com",
    "TenantId": "YOUR-ENTRA-TENANT-ID",
    "ClientId": "YOUR-ENTRA-CLIENT-ID",
    "Authority": "https://login.microsoftonline.com/YOUR-TENANT-ID/v2.0",
    "ValidAudience": "api://YOUR-CLIENT-ID",
    "IsAgOneGateway": false
  }
}
```

That's it! The middleware handles everything else.

## Blazor WASM Client Setup

In your Blazor WASM client's `Program.cs`:

```csharp
using AgOne.Sso.Blazor;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register cookie handler (ensures auth cookies are sent with requests)
builder.Services.AddTransient<CookieHandler>();

// Configure HttpClient with cookie support
builder.Services
    .AddHttpClient("Backend", client =>
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<CookieHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Backend"));

// Register SSO auth state provider
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    new AgOneSsoAuthStateProvider(sp.GetRequiredService<HttpClient>()));
```

Then wrap your `App.razor` (or `Routes.razor`) with `<CascadingAuthenticationState>`:

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
                <NotAuthorized>
                    <p>Redirecting to login...</p>
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
        <NotFound>
            <p>Page not found</p>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

## For AG ONE Gateway Itself

The same middleware works on AG ONE too. The key difference is `IsAgOneGateway = true`:

```json
{
  "AgOneSso": {
    "IsAgOneGateway": true,
    "TenantId": "YOUR-ENTRA-TENANT-ID",
    "ClientId": "YOUR-ENTRA-CLIENT-ID",
    "Authority": "https://login.microsoftonline.com/YOUR-TENANT-ID/v2.0",
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

When `IsAgOneGateway` is true:
- The middleware does NOT call external APIs for token refresh
- AG ONE handles its own token lifecycle internally via your existing Entra ID refresh logic
- The `/api/auth/external/validate` endpoint is kept anonymous so other products can call it

## Configuration Reference

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `AgOneBaseUrl` | string | (required) | Base URL of AG ONE API |
| `AgOneLoginUrl` | string | AgOneBaseUrl | Login page URL for redirects |
| `TokenValidateEndpoint` | string | `api/auth/external/validate` | AG ONE token refresh API path |
| `TenantId` | string | (required) | Entra ID Tenant ID |
| `ClientId` | string | (required) | Entra ID Client ID |
| `Authority` | string | auto from TenantId | OIDC authority URL |
| `ValidAudience` | string | ClientId | Expected JWT audience |
| `SessionCookieName` | string | `agone_sso_token` | Session cookie name |
| `LaunchCookieName` | string | `agone_launch_token` | Launch cookie name |
| `SessionCookieLifetime` | TimeSpan | 60 min | Cookie expiry |
| `CookieDomain` | string | (auto) | Shared cookie domain |
| `CookieSameSite` | enum | `None` | SameSite cookie mode |
| `RefreshBufferMinutes` | int | 5 | Minutes before expiry to start refresh |
| `IsAgOneGateway` | bool | `false` | Set true for AG ONE itself |
| `AcceptTokenFromQueryString` | bool | `true` | Accept token in URL query |
| `AnonymousPaths` | string[] | `[]` | Paths that skip auth |

## Scenarios Handled

| Scenario | Behavior |
|----------|----------|
| First visit from Product Launcher | Token extracted from launch cookie, validated, session cookie set |
| Subsequent page requests | Token from session cookie, validated locally |
| Blazor WASM API calls | Token from cookie (via CookieHandler), validated |
| Token valid and fresh | ClaimsPrincipal set, request continues |
| Token expiring within 5 min | Proactive refresh via AG ONE API, cookie updated |
| Token expired | Refresh via AG ONE API; if fails → redirect/401 |
| Refresh token also expired | AG ONE returns null → redirect to login |
| No token at all | Redirect to AG ONE login (pages) or 401 (API) |
| Direct URL access (no token) | Redirect to AG ONE login with returnUrl |
| Static files (.js, .css, .wasm) | Skipped by middleware — always served |
| Blazor framework files | Skipped by middleware — always served |
| Health check / public endpoints | Skipped via AnonymousPaths config |
| Token in query string | Accepted, session cookie set, redirected to clean URL |
| SignalR / _blazor connections | Cookie-based auth works automatically |
| Cross-site cookie issues | SameSite=None + Secure=true handles this |
| Invalid/tampered JWT | Rejected immediately, redirect to login |
| AG ONE gateway itself | Same middleware with IsAgOneGateway=true, no external refresh calls |

## API Endpoints (Auto-Mapped)

These are mapped when you call `app.MapAgOneSsoEndpoints()`:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/sso-user-info` | GET | Returns current user's claims (used by Blazor WASM AuthStateProvider) |
| `/api/auth/sso-logout` | POST | Clears SSO cookies, returns login redirect URL |
| `/api/auth/sso-health` | GET | Health check for the SSO middleware |

## Project Structure

```
src/AgOne.Sso.Middleware/
├── AgOneSsoMiddleware.cs              # Main middleware (the core)
├── Models/
│   ├── AgOneSsoOptions.cs             # Configuration options
│   ├── ExternalTokenValidationRequest.cs  # Request to AG ONE API
│   ├── TokenValidationResponse.cs     # Response from AG ONE API
│   └── SsoUserInfo.cs                 # User info for Blazor WASM
├── Services/
│   ├── IAgOneTokenClient.cs           # Token client interface
│   └── AgOneTokenClient.cs            # HTTP client for AG ONE refresh API
├── Extensions/
│   ├── ServiceCollectionExtensions.cs  # DI registration (.AddAgOneSso)
│   ├── ApplicationBuilderExtensions.cs # Pipeline registration (.UseAgOneSso)
│   └── EndpointRouteBuilderExtensions.cs # Endpoint mapping (.MapAgOneSsoEndpoints)
└── Blazor/
    ├── AgOneSsoAuthStateProvider.cs    # Blazor WASM auth state
    └── CookieHandler.cs               # Cookie-aware HTTP handler

examples/
├── ProductApp.Server/                 # Example: Product backend setup
├── ProductApp.Client/                 # Example: Product Blazor WASM setup
└── AgOneGateway.Server/               # Example: AG ONE gateway setup
```

## Requirements

- .NET 8.0+
- Entra ID (Azure AD) tenant with registered application
- AG ONE central gateway running with the `/api/auth/external/validate` endpoint
