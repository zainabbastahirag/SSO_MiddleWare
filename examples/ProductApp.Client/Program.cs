// ═══════════════════════════════════════════════════════════════════════════════
// EXAMPLE: Product App Blazor WASM Client (e.g., AGOneWork, AGOneLearn)
// ═══════════════════════════════════════════════════════════════════════════════
// This is the Blazor WebAssembly client that uses the SSO middleware's auth state.
// Copy this pattern into your Blazor WASM client's Program.cs.
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using AgOne.Sso.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ─── Step 1: Register the WASM cookie handler ───
// This ensures cookies (including the SSO token) are sent with every HTTP request
builder.Services.AddTransient<WasmCookieHandler>();

// ─── Step 2: Configure HttpClient with cookie handler ───
builder.Services
    .AddHttpClient("Backend", client =>
    {
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
    })
    .AddHttpMessageHandler<WasmCookieHandler>();

// Register default HttpClient using the named client
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Backend"));

// ─── Step 3: Register the AG ONE SSO auth state provider ───
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    return new AgOneSsoAuthStateProvider(httpClient);
});

await builder.Build().RunAsync();

// ═══════════════════════════════════════════════════════════════════════════════
// WASM-specific cookie handler — put this in a separate file in your WASM project
// ═══════════════════════════════════════════════════════════════════════════════
// File: WasmCookieHandler.cs
//
// using Microsoft.AspNetCore.Components.WebAssembly.Http;
//
// public class WasmCookieHandler : DelegatingHandler
// {
//     protected override Task<HttpResponseMessage> SendAsync(
//         HttpRequestMessage request, CancellationToken cancellationToken)
//     {
//         // Tell the browser to include cookies in cross-origin requests
//         request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
//         request.Headers.Add("X-Requested-With", "XMLHttpRequest");
//         return base.SendAsync(request, cancellationToken);
//     }
// }
// ═══════════════════════════════════════════════════════════════════════════════
