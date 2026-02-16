using System.ClientModel;
using System.Runtime.CompilerServices;
using AgAiHub.Configuration;
using AgAiHub.Models;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace AgAiHub.Services;

/// <summary>
/// Direct OpenAI (non-Azure) implementation of IAiProvider.
/// </summary>
public class OpenAiProvider : IAiProvider
{
    private readonly OpenAIClient _client;
    private readonly AiHubOptions _options;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(IOptions<AiHubOptions> options, ILogger<OpenAiProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        var apiKey = _options.OpenAi.ApiKey;
        _client = new OpenAIClient(apiKey);
    }

    public async Task<ChatResponse> ChatAsync(
        string model,
        List<AiChatMessage> messages,
        float temperature,
        int maxTokens,
        CancellationToken ct = default)
    {
        var resolvedModel = !string.IsNullOrEmpty(model) ? model : _options.DefaultChatModel;
        var chatClient = _client.GetChatClient(resolvedModel);

        var chatMessages = messages.Select(ToChatMessage).ToList();

        var completionOptions = new ChatCompletionOptions
        {
            Temperature = temperature,
            MaxOutputTokenCount = maxTokens
        };

        _logger.LogInformation("Chat request: model={Model}, messages={Count}", resolvedModel, messages.Count);

        var result = await chatClient.CompleteChatAsync(chatMessages, completionOptions, ct);
        var completion = result.Value;

        return new ChatResponse
        {
            Content = completion.Content?.FirstOrDefault()?.Text ?? "",
            Model = resolvedModel,
            FinishReason = completion.FinishReason.ToString(),
            Usage = new UsageInfo
            {
                PromptTokens = completion.Usage.InputTokenCount,
                CompletionTokens = completion.Usage.OutputTokenCount,
                TotalTokens = completion.Usage.TotalTokenCount
            }
        };
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model,
        List<AiChatMessage> messages,
        float temperature,
        int maxTokens,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolvedModel = !string.IsNullOrEmpty(model) ? model : _options.DefaultChatModel;
        var chatClient = _client.GetChatClient(resolvedModel);

        var chatMessages = messages.Select(ToChatMessage).ToList();

        var completionOptions = new ChatCompletionOptions
        {
            Temperature = temperature,
            MaxOutputTokenCount = maxTokens
        };

        _logger.LogInformation("Chat stream request: model={Model}, messages={Count}", resolvedModel, messages.Count);

        AsyncCollectionResult<StreamingChatCompletionUpdate> stream =
            chatClient.CompleteChatStreamingAsync(chatMessages, completionOptions, ct);

        await foreach (var update in stream)
        {
            if (ct.IsCancellationRequested) yield break;

            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
            }
        }
    }

    public async Task<EmbeddingResponse> EmbedAsync(
        string model,
        List<string> inputs,
        CancellationToken ct = default)
    {
        var resolvedModel = !string.IsNullOrEmpty(model) ? model : _options.DefaultEmbeddingModel;
        var embeddingClient = _client.GetEmbeddingClient(resolvedModel);

        _logger.LogInformation("Embedding request: model={Model}, inputs={Count}", resolvedModel, inputs.Count);

        var result = await embeddingClient.GenerateEmbeddingsAsync(inputs, cancellationToken: ct);

        var embeddings = result.Value
            .Select(e => e.ToFloats().ToArray())
            .ToList();

        return new EmbeddingResponse
        {
            Embeddings = embeddings,
            Model = resolvedModel,
            Usage = new UsageInfo
            {
                PromptTokens = result.Value.Usage.InputTokenCount,
                TotalTokens = result.Value.Usage.TotalTokenCount
            }
        };
    }

    private static OpenAI.Chat.ChatMessage ToChatMessage(AiChatMessage m)
    {
        return m.Role.ToLowerInvariant() switch
        {
            "system" => new SystemChatMessage(m.Content),
            "assistant" => new AssistantChatMessage(m.Content),
            _ => new UserChatMessage(m.Content)
        };
    }
}
