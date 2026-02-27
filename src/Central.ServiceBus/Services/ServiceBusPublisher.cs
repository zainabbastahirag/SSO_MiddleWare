using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Central.ServiceBus.Configuration;
using Central.ServiceBus.Interfaces;
using Central.ServiceBus.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Central.ServiceBus.Services;

public class ServiceBusPublisher : IServiceBusPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusPublisher> _logger;
    private readonly ServiceBusConfiguration _config;

    public ServiceBusPublisher(
        ServiceBusClient client,
        IOptions<ServiceBusConfiguration> options,
        ILogger<ServiceBusPublisher> logger)
    {
        _client = client;
        _config = options.Value;
        _logger = logger;
        _sender = _client.CreateSender(_config.TopicName);
    }

    public async Task PublishAsync<T>(T payload, string source, string? correlationId = null,
        Dictionary<string, string>? properties = null, CancellationToken cancellationToken = default) where T : class
    {
        var wrapper = new ServiceBusMessageWrapper<T>
        {
            Source = source,
            CorrelationId = correlationId,
            Payload = payload,
            Properties = properties ?? new Dictionary<string, string>()
        };

        await PublishAsync(wrapper, cancellationToken);
    }

    public async Task PublishAsync<T>(ServiceBusMessageWrapper<T> message,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var sbMessage = new ServiceBusMessage(json)
            {
                MessageId = message.MessageId,
                ContentType = "application/json",
                Subject = message.MessageType,
                CorrelationId = message.CorrelationId ?? string.Empty
            };

            sbMessage.ApplicationProperties["source"] = message.Source;
            sbMessage.ApplicationProperties["messageType"] = message.MessageType;
            sbMessage.ApplicationProperties["timestamp"] = message.Timestamp.ToString("O");

            foreach (var prop in message.Properties)
            {
                sbMessage.ApplicationProperties[prop.Key] = prop.Value;
            }

            await _sender.SendMessageAsync(sbMessage, cancellationToken);

            _logger.LogInformation(
                "Published message {MessageId} of type {MessageType} from {Source} to topic {Topic}",
                message.MessageId, message.MessageType, message.Source, _config.TopicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish message {MessageId} of type {MessageType} to topic {Topic}",
                message.MessageId, message.MessageType, _config.TopicName);
            throw;
        }
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> payloads, string source,
        string? correlationId = null, CancellationToken cancellationToken = default) where T : class
    {
        var messages = new List<ServiceBusMessage>();

        foreach (var payload in payloads)
        {
            var wrapper = new ServiceBusMessageWrapper<T>
            {
                Source = source,
                CorrelationId = correlationId,
                Payload = payload
            };

            var json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var sbMessage = new ServiceBusMessage(json)
            {
                MessageId = wrapper.MessageId,
                ContentType = "application/json",
                Subject = wrapper.MessageType,
                CorrelationId = wrapper.CorrelationId ?? string.Empty
            };

            sbMessage.ApplicationProperties["source"] = wrapper.Source;
            sbMessage.ApplicationProperties["messageType"] = wrapper.MessageType;
            sbMessage.ApplicationProperties["timestamp"] = wrapper.Timestamp.ToString("O");

            messages.Add(sbMessage);
        }

        try
        {
            using var batch = await _sender.CreateMessageBatchAsync(cancellationToken);
            foreach (var msg in messages)
            {
                if (!batch.TryAddMessage(msg))
                {
                    _logger.LogWarning("Batch is full. Sending current batch and creating a new one.");
                    await _sender.SendMessagesAsync(batch, cancellationToken);
                    using var newBatch = await _sender.CreateMessageBatchAsync(cancellationToken);
                    if (!newBatch.TryAddMessage(msg))
                    {
                        throw new InvalidOperationException("Message is too large for an empty batch.");
                    }
                }
            }

            await _sender.SendMessagesAsync(batch, cancellationToken);

            _logger.LogInformation(
                "Published batch of {Count} messages from {Source} to topic {Topic}",
                messages.Count, source, _config.TopicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch of messages to topic {Topic}", _config.TopicName);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
