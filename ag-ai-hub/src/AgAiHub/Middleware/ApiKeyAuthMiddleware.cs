using AgAiHub.Configuration;
using Microsoft.Extensions.Options;

namespace AgAiHub.Middleware;

/// <summary>
/// API key authentication middleware.
/// Every product has its own API key configured in appsettings.json.
/// Requests must include: X-Api-Key header + X-Product-Code header.
/// </summary>
public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    // Paths that don't require auth
    private static readonly string[] OpenPaths = { "/api/health", "/swagger", "/favicon.ico" };

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<AiHubOptions> options)
    {
        var path = context.Request.Path.Value ?? "/";

        // Skip auth for open paths
        if (OpenPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Only protect /api/ routes
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Extract headers
        var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        var productCode = context.Request.Headers["X-Product-Code"].FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(productCode))
        {
            _logger.LogWarning("Missing X-Api-Key or X-Product-Code header from {IP}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing X-Api-Key and X-Product-Code headers" });
            return;
        }

        // Validate product + key
        var opts = options.Value;
        if (!opts.Products.TryGetValue(productCode, out var productConfig))
        {
            _logger.LogWarning("Unknown product code: {ProductCode}", productCode);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = $"Unknown product: {productCode}" });
            return;
        }

        if (!productConfig.Enabled)
        {
            _logger.LogWarning("Product {ProductCode} is disabled", productCode);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = $"Product {productCode} is disabled" });
            return;
        }

        if (productConfig.ApiKey != apiKey)
        {
            _logger.LogWarning("Invalid API key for product {ProductCode}", productCode);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        // Store product code in HttpContext for controllers to access
        context.Items["ProductCode"] = productCode;
        context.Items["ProductConfig"] = productConfig;

        await _next(context);
    }
}

/// <summary>
/// Extension to easily get the authenticated product code from HttpContext.
/// </summary>
public static class HttpContextProductExtensions
{
    public static string GetProductCode(this HttpContext context)
    {
        return context.Items["ProductCode"] as string ?? "unknown";
    }

    public static ProductAiConfig? GetProductConfig(this HttpContext context)
    {
        return context.Items["ProductConfig"] as ProductAiConfig;
    }
}
