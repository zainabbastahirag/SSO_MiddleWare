using AGOneLearn.UI.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Example: builder.RootComponents.Add<App>("#app");
// Example: builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();

// CookieHandler ensures the HttpOnly SSO cookie is sent with every request
builder.Services.AddTransient<CookieHandler>();

builder.Services.AddHttpClient("Backend",
        client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<CookieHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Backend"));

// AuthService calls /api/auth/sso-user-info — no dependency on AuthenticationStateProvider
builder.Services.AddScoped<IAuthService, AuthService>();

// AuthenticationStateProvider depends on IAuthService (one-way, no cycle)
builder.Services.AddScoped<AGOneAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<AGOneAuthStateProvider>());

await builder.Build().RunAsync();

/// <summary>
/// Attaches browser credentials (cookies) to every outgoing HTTP request
/// so the server-side SSO middleware receives the HttpOnly auth cookie.
/// </summary>
public class CookieHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
