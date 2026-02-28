namespace AgAiHub.Configuration;

/// <summary>
/// Root configuration for the AI Hub.
/// </summary>
public class AiHubOptions
{
    public const string SectionName = "AiHub";

    /// <summary>Azure OpenAI settings.</summary>
    public AzureOpenAiSettings AzureOpenAi { get; set; } = new();

    /// <summary>OpenAI settings (alternative to Azure).</summary>
    public OpenAiSettings OpenAi { get; set; } = new();

    /// <summary>Which provider to use: "AzureOpenAi" or "OpenAi".</summary>
    public string Provider { get; set; } = "AzureOpenAi";

    /// <summary>Default model for chat completions.</summary>
    public string DefaultChatModel { get; set; } = "gpt-4o-mini";

    /// <summary>Default model for embeddings.</summary>
    public string DefaultEmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>Default temperature.</summary>
    public float DefaultTemperature { get; set; } = 0.7f;

    /// <summary>Default max tokens.</summary>
    public int DefaultMaxTokens { get; set; } = 2048;

    /// <summary>Per-product configuration overrides.</summary>
    public Dictionary<string, ProductAiConfig> Products { get; set; } = new();
}

/// <summary>
/// Per-product AI configuration. Each product can override defaults.
/// </summary>
public class ProductAiConfig
{
    /// <summary>Product display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>API key that this product uses to authenticate with the AI Hub.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Default system prompt for this product's chat requests.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Override default chat model for this product.</summary>
    public string? ChatModel { get; set; }

    /// <summary>Override default embedding model for this product.</summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>Override temperature.</summary>
    public float? Temperature { get; set; }

    /// <summary>Override max tokens.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Max requests per minute (0 = unlimited).</summary>
    public int RateLimitPerMinute { get; set; } = 0;

    /// <summary>Whether this product is enabled.</summary>
    public bool Enabled { get; set; } = true;
}

public class AzureOpenAiSettings
{
    /// <summary>Azure OpenAI endpoint (e.g. "https://myresource.openai.azure.com/").</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Azure OpenAI API key.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Deployment name for chat model (maps to your Azure deployment).</summary>
    public string ChatDeployment { get; set; } = "gpt-4o-mini";

    /// <summary>Deployment name for embedding model.</summary>
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
}

public class OpenAiSettings
{
    /// <summary>OpenAI API key.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Optional organization ID.</summary>
    public string? OrganizationId { get; set; }
}
