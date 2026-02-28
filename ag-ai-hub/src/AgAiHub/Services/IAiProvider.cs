using AgAiHub.Models;

namespace AgAiHub.Services;

/// <summary>
/// Abstraction over AI providers (Azure OpenAI, OpenAI, etc.).
/// Swap implementations without changing any controller or service code.
/// </summary>
public interface IAiProvider
{
    /// <summary>Send a chat completion request.</summary>
    Task<ChatResponse> ChatAsync(
        string model,
        List<AiChatMessage> messages,
        float temperature,
        int maxTokens,
        CancellationToken ct = default);

    /// <summary>Stream a chat completion via IAsyncEnumerable.</summary>
    IAsyncEnumerable<string> ChatStreamAsync(
        string model,
        List<AiChatMessage> messages,
        float temperature,
        int maxTokens,
        CancellationToken ct = default);

    /// <summary>Generate embeddings for the given inputs.</summary>
    Task<EmbeddingResponse> EmbedAsync(
        string model,
        List<string> inputs,
        CancellationToken ct = default);
}
