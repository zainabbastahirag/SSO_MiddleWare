using AGOneLearn.UI.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Example: builder.RootComponents.Add<App>("#app");
// Example: builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();

// Register AuthService FIRST (no dependency on AuthenticationStateProvider)
builder.Services.AddScoped<IAuthService, AuthService>();

// Register the custom AuthenticationStateProvider (depends only on IAuthService)
builder.Services.AddScoped<AGOneAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<AGOneAuthStateProvider>());

// Register HttpClient, other services, etc.
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
