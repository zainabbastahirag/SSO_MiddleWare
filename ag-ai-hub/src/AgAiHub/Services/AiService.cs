using AgAiHub.Configuration;
using AgAiHub.Models;
using Microsoft.Extensions.Options;

namespace AgAiHub.Services;

/// <summary>
/// Core AI service — the single entry point for all AI operations.
/// Resolves product-specific config, delegates to the AI provider, tracks usage.
/// </summary>
public class AiService
{
    private readonly IAiProvider _provider;
    private readonly AiHubOptions _options;
    private readonly UsageTracker _usage;
    private readonly ILogger<AiService> _logger;

    public AiService(IAiProvider provider, IOptions<AiHubOptions> options, UsageTracker usage, ILogger<AiService> logger)
    {
        _provider = provider;
        _options = options.Value;
        _usage = usage;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // CHAT
    // ═══════════════════════════════════════════════════════════════════

    public async Task<ChatResponse> ChatAsync(string productCode, ChatRequest request, CancellationToken ct = default)
    {
        var config = GetProductConfig(productCode);

        var model = request.Model ?? config?.ChatModel ?? _options.DefaultChatModel;
        var temperature = request.Temperature ?? config?.Temperature ?? _options.DefaultTemperature;
        var maxTokens = request.MaxTokens ?? config?.MaxTokens ?? _options.DefaultMaxTokens;

        var messages = BuildMessages(request.Messages, request.SystemPrompt, config?.SystemPrompt);

        _logger.LogInformation("Chat: product={Product}, model={Model}, msgs={Count}",
            productCode, model, messages.Count);

        var response = await _provider.ChatAsync(model, messages, temperature, maxTokens, ct);

        _usage.Track(productCode, "chat", model, response.Usage);

        return response;
    }

    public IAsyncEnumerable<string> ChatStreamAsync(string productCode, ChatRequest request, CancellationToken ct = default)
    {
        var config = GetProductConfig(productCode);

        var model = request.Model ?? config?.ChatModel ?? _options.DefaultChatModel;
        var temperature = request.Temperature ?? config?.Temperature ?? _options.DefaultTemperature;
        var maxTokens = request.MaxTokens ?? config?.MaxTokens ?? _options.DefaultMaxTokens;

        var messages = BuildMessages(request.Messages, request.SystemPrompt, config?.SystemPrompt);

        _logger.LogInformation("Chat stream: product={Product}, model={Model}, msgs={Count}",
            productCode, model, messages.Count);

        _usage.Track(productCode, "chat_stream", model, null);

        return _provider.ChatStreamAsync(model, messages, temperature, maxTokens, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    // EMBEDDINGS
    // ═══════════════════════════════════════════════════════════════════

    public async Task<EmbeddingResponse> EmbedAsync(string productCode, EmbeddingRequest request, CancellationToken ct = default)
    {
        var config = GetProductConfig(productCode);

        var model = request.Model ?? config?.EmbeddingModel ?? _options.DefaultEmbeddingModel;

        _logger.LogInformation("Embed: product={Product}, model={Model}, inputs={Count}",
            productCode, model, request.Inputs.Count);

        var response = await _provider.EmbedAsync(model, request.Inputs, ct);

        _usage.Track(productCode, "embedding", model, response.Usage);

        return response;
    }

    // ═══════════════════════════════════════════════════════════════════
    // DOCUMENT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════

    public async Task<DocumentResponse> ProcessDocumentAsync(string productCode, DocumentRequest request, CancellationToken ct = default)
    {
        var config = GetProductConfig(productCode);
        var model = request.Model ?? config?.ChatModel ?? _options.DefaultChatModel;
        var temperature = config?.Temperature ?? _options.DefaultTemperature;
        var maxTokens = config?.MaxTokens ?? _options.DefaultMaxTokens;

        var systemPrompt = request.Operation.ToLowerInvariant() switch
        {
            "summarize" => "You are an expert at summarizing documents. Provide a clear, concise summary. Preserve all key facts, figures, and conclusions.",
            "extract" => $"You are an expert at extracting information from documents. Extract the following: {request.ExtractionTarget ?? "key information"}. Return structured, clear results.",
            "qa" => "You are an expert at answering questions based on provided documents. Answer based ONLY on the document content. If the answer is not in the document, say so.",
            "classify" => $"You are an expert at document classification. Classify the document into one of these categories: {string.Join(", ", request.Categories ?? new List<string>())}. Return only the category name and a one-line explanation.",
            _ => "You are a helpful AI assistant. Process the following document as requested."
        };

        var userContent = request.Operation.ToLowerInvariant() switch
        {
            "qa" => $"Document:\n\n{request.Content}\n\nQuestion: {request.Question}",
            _ => request.Content
        };

        var messages = new List<AiChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = userContent }
        };

        _logger.LogInformation("Document {Op}: product={Product}, model={Model}, length={Len}",
            request.Operation, productCode, model, request.Content.Length);

        var chatResponse = await _provider.ChatAsync(model, messages, temperature, maxTokens, ct);

        _usage.Track(productCode, $"document_{request.Operation}", model, chatResponse.Usage);

        return new DocumentResponse
        {
            Result = chatResponse.Content,
            Operation = request.Operation,
            Model = chatResponse.Model,
            Usage = chatResponse.Usage
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private ProductAiConfig? GetProductConfig(string productCode)
    {
        _options.Products.TryGetValue(productCode, out var config);
        return config;
    }

    private static List<AiChatMessage> BuildMessages(List<AiChatMessage> messages, string? requestSystemPrompt, string? productSystemPrompt)
    {
        var result = new List<AiChatMessage>();

        var systemPrompt = requestSystemPrompt ?? productSystemPrompt;
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            result.Add(new AiChatMessage { Role = "system", Content = systemPrompt });
        }

        // Add user messages (skip any system messages from the request if we already added one)
        foreach (var msg in messages)
        {
            if (msg.Role == "system" && !string.IsNullOrEmpty(systemPrompt))
                continue;
            result.Add(msg);
        }

        return result;
    }
}
