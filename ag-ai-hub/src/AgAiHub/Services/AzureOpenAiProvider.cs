using System.ClientModel;
using System.Runtime.CompilerServices;
using AgAiHub.Configuration;
using AgAiHub.Models;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace AgAiHub.Services;

/// <summary>
/// Azure OpenAI implementation of IAiProvider.
/// Uses the Azure.AI.OpenAI SDK.
/// </summary>
public class AzureOpenAiProvider : IAiProvider
{
    private readonly AzureOpenAIClient _client;
    private readonly AiHubOptions _options;
    private readonly ILogger<AzureOpenAiProvider> _logger;

    public AzureOpenAiProvider(IOptions<AiHubOptions> options, ILogger<AzureOpenAiProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        var azureSettings = _options.AzureOpenAi;
        _client = new AzureOpenAIClient(
            new Uri(azureSettings.Endpoint),
            new AzureKeyCredential(azureSettings.ApiKey));
    }

    public async Task<ChatResponse> ChatAsync(
        string model,
        List<AiChatMessage> messages,
        float temperature,
        int maxTokens,
        CancellationToken ct = default)
    {
        var deployment = ResolveDeployment(model);
        var chatClient = _client.GetChatClient(deployment);

        var chatMessages = messages.Select(ToChatMessage).ToList();

        var completionOptions = new ChatCompletionOptions
        {
            Temperature = temperature,
            MaxOutputTokenCount = maxTokens
        };

        _logger.LogInformation("Chat request: model={Model}, messages={Count}", deployment, messages.Count);

        var result = await chatClient.CompleteChatAsync(chatMessages, completionOptions, ct);
        var completion = result.Value;

        return new ChatResponse
        {
            Content = completion.Content?.FirstOrDefault()?.Text ?? "",
            Model = deployment,
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
        var deployment = ResolveDeployment(model);
        var chatClient = _client.GetChatClient(deployment);

        var chatMessages = messages.Select(ToChatMessage).ToList();

        var completionOptions = new ChatCompletionOptions
        {
            Temperature = temperature,
            MaxOutputTokenCount = maxTokens
        };

        _logger.LogInformation("Chat stream request: model={Model}, messages={Count}", deployment, messages.Count);

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
        var deployment = ResolveEmbeddingDeployment(model);
        var embeddingClient = _client.GetEmbeddingClient(deployment);

        _logger.LogInformation("Embedding request: model={Model}, inputs={Count}", deployment, inputs.Count);

        var result = await embeddingClient.GenerateEmbeddingsAsync(inputs, cancellationToken: ct);

        var embeddings = result.Value
            .Select(e => e.ToFloats().ToArray())
            .ToList();

        return new EmbeddingResponse
        {
            Embeddings = embeddings,
            Model = deployment,
            Usage = new UsageInfo
            {
                PromptTokens = result.Value.Usage.InputTokenCount,
                TotalTokens = result.Value.Usage.TotalTokenCount
            }
        };
    }

    private string ResolveDeployment(string? model)
    {
        if (!string.IsNullOrEmpty(model)) return model;
        return !string.IsNullOrEmpty(_options.AzureOpenAi.ChatDeployment)
            ? _options.AzureOpenAi.ChatDeployment
            : _options.DefaultChatModel;
    }

    private string ResolveEmbeddingDeployment(string? model)
    {
        if (!string.IsNullOrEmpty(model)) return model;
        return !string.IsNullOrEmpty(_options.AzureOpenAi.EmbeddingDeployment)
            ? _options.AzureOpenAi.EmbeddingDeployment
            : _options.DefaultEmbeddingModel;
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
