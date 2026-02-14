namespace AgOne.Sso.Blazor;

/// <summary>
/// Delegating handler that ensures cookies are sent with every HTTP request from Blazor WASM.
/// 
/// IMPORTANT: This is a BASE handler. In your Blazor WASM client project, you should create
/// a subclass that adds BrowserRequestCredentials.Include. See the example below.
/// 
/// For Blazor WASM clients, create this handler in your WASM project:
/// <code>
/// using Microsoft.AspNetCore.Components.WebAssembly.Http;
/// 
/// public class WasmCookieHandler : DelegatingHandler
/// {
///     protected override Task&lt;HttpResponseMessage&gt; SendAsync(
///         HttpRequestMessage request, CancellationToken cancellationToken)
///     {
///         request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
///         request.Headers.Add("X-Requested-With", "XMLHttpRequest");
///         return base.SendAsync(request, cancellationToken);
///     }
/// }
/// </code>
/// 
/// This base version adds the X-Requested-With header for API request detection
/// and can be used in non-WASM HTTP clients (e.g., server-to-server calls).
/// </summary>
public class CookieHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Mark as AJAX request so the middleware can distinguish API calls from page requests
        if (!request.Headers.Contains("X-Requested-With"))
        {
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
