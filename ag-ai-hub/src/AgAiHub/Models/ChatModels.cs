using System.Text.Json.Serialization;

namespace AgAiHub.Models;

// ─────────────────────────────────────────────────────────────────────────────
// CHAT COMPLETION
// ─────────────────────────────────────────────────────────────────────────────

public class ChatRequest
{
    /// <summary>The conversation messages.</summary>
    [JsonPropertyName("messages")]
    public List<AiChatMessage> Messages { get; set; } = new();

    /// <summary>Optional system prompt override. If empty, uses product's default system prompt.</summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    /// <summary>Model to use (e.g. "gpt-4o", "gpt-4o-mini"). If empty, uses product's default.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Temperature (0.0 - 2.0). Lower = more deterministic.</summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>Max tokens in the response.</summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    /// <summary>If true, response is streamed via SSE.</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;

    /// <summary>Optional context/metadata from the calling product.</summary>
    [JsonPropertyName("context")]
    public Dictionary<string, string>? Context { get; set; }
}

public class AiChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user"; // "system", "user", "assistant"

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public class ChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// EMBEDDINGS
// ─────────────────────────────────────────────────────────────────────────────

public class EmbeddingRequest
{
    /// <summary>Text(s) to generate embeddings for.</summary>
    [JsonPropertyName("inputs")]
    public List<string> Inputs { get; set; } = new();

    /// <summary>Model to use. If empty, uses default embedding model.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

public class EmbeddingResponse
{
    [JsonPropertyName("embeddings")]
    public List<float[]> Embeddings { get; set; } = new();

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// DOCUMENT ANALYSIS (summarize, extract, Q&A over text)
// ─────────────────────────────────────────────────────────────────────────────

public class DocumentRequest
{
    /// <summary>The operation to perform.</summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "summarize"; // "summarize", "extract", "qa", "classify"

    /// <summary>The document/text content.</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>For "qa" operation — the question to answer from the document.</summary>
    [JsonPropertyName("question")]
    public string? Question { get; set; }

    /// <summary>For "extract" — what to extract (e.g. "key dates", "action items").</summary>
    [JsonPropertyName("extractionTarget")]
    public string? ExtractionTarget { get; set; }

    /// <summary>For "classify" — the categories to classify into.</summary>
    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    /// <summary>Model to use. If empty, uses product default.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Optional context from the product.</summary>
    [JsonPropertyName("context")]
    public Dictionary<string, string>? Context { get; set; }
}

public class DocumentResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// SHARED
// ─────────────────────────────────────────────────────────────────────────────

public class UsageInfo
{
    [JsonPropertyName("promptTokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completionTokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("totalTokens")]
    public int TotalTokens { get; set; }
}

public class AiErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}
