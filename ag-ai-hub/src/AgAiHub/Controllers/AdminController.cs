using AgAiHub.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgAiHub.Controllers;

[ApiController]
[Route("api")]
public class AdminController : ControllerBase
{
    private readonly UsageTracker _usage;

    public AdminController(UsageTracker usage)
    {
        _usage = usage;
    }

    /// <summary>Health check — always open, no auth required.</summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "AG AI Hub",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }

    /// <summary>Get usage stats for all products.</summary>
    [HttpGet("usage")]
    public IActionResult GetUsage()
    {
        var all = _usage.GetAllUsage();
        return Ok(new
        {
            products = all.Select(kv => new
            {
                product = kv.Key,
                requests = kv.Value.TotalRequests,
                promptTokens = kv.Value.TotalPromptTokens,
                completionTokens = kv.Value.TotalCompletionTokens,
                totalTokens = kv.Value.TotalTokens,
                lastRequest = kv.Value.LastRequestUtc
            }),
            totalRequests = all.Values.Sum(u => u.TotalRequests),
            totalTokens = all.Values.Sum(u => u.TotalTokens)
        });
    }

    /// <summary>Get usage stats for a specific product.</summary>
    [HttpGet("usage/{productCode}")]
    public IActionResult GetProductUsage(string productCode)
    {
        var usage = _usage.GetUsage(productCode);
        if (usage == null)
            return NotFound(new { error = $"No usage data for product: {productCode}" });

        return Ok(new
        {
            product = productCode,
            requests = usage.TotalRequests,
            promptTokens = usage.TotalPromptTokens,
            completionTokens = usage.TotalCompletionTokens,
            totalTokens = usage.TotalTokens,
            lastRequest = usage.LastRequestUtc
        });
    }
}
