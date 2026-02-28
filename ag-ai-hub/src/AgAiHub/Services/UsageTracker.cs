using System.Collections.Concurrent;
using AgAiHub.Models;

namespace AgAiHub.Services;

/// <summary>
/// Tracks AI usage per product. In-memory for the starter template.
/// Replace with DB/Redis for production persistence.
/// </summary>
public class UsageTracker
{
    private readonly ConcurrentDictionary<string, ProductUsage> _usage = new();
    private readonly ILogger<UsageTracker> _logger;

    public UsageTracker(ILogger<UsageTracker> logger)
    {
        _logger = logger;
    }

    public void Track(string productCode, string operation, string model, UsageInfo? usage)
    {
        var entry = _usage.GetOrAdd(productCode, _ => new ProductUsage { ProductCode = productCode });

        Interlocked.Increment(ref entry.TotalRequests);

        if (usage != null)
        {
            Interlocked.Add(ref entry.TotalPromptTokens, usage.PromptTokens);
            Interlocked.Add(ref entry.TotalCompletionTokens, usage.CompletionTokens);
            Interlocked.Add(ref entry.TotalTokens, usage.TotalTokens);
        }

        entry.LastRequestUtc = DateTime.UtcNow;

        _logger.LogDebug("Usage: product={Product}, op={Op}, model={Model}, tokens={Tokens}",
            productCode, operation, model, usage?.TotalTokens ?? 0);
    }

    /// <summary>Get usage stats for a specific product.</summary>
    public ProductUsage? GetUsage(string productCode)
    {
        _usage.TryGetValue(productCode, out var usage);
        return usage;
    }

    /// <summary>Get usage stats for all products.</summary>
    public Dictionary<string, ProductUsage> GetAllUsage()
    {
        return _usage.ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}

public class ProductUsage
{
    public string ProductCode { get; set; } = "";
    public int TotalRequests;
    public int TotalPromptTokens;
    public int TotalCompletionTokens;
    public int TotalTokens;
    public DateTime LastRequestUtc { get; set; }
}
