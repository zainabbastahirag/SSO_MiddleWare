using Azure.Messaging.ServiceBus;
using Central.ServiceBus.Configuration;
using Central.ServiceBus.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Central.ServiceBus.Services;

public class ServiceBusSubscriber : IServiceBusSubscriber, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusConfiguration _config;
    private readonly IMessageHandler _messageHandler;
    private readonly ILogger<ServiceBusSubscriber> _logger;
    private ServiceBusProcessor? _processor;
    private bool _isListening;

    public bool IsListening => _isListening;

    public ServiceBusSubscriber(
        ServiceBusClient client,
        IOptions<ServiceBusConfiguration> options,
        IMessageHandler messageHandler,
        ILogger<ServiceBusSubscriber> logger)
    {
        _client = client;
        _config = options.Value;
        _messageHandler = messageHandler;
        _logger = logger;
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        if (_isListening)
        {
            _logger.LogWarning("Subscriber is already listening on {Topic}/{Subscription}",
                _config.TopicName, _config.SubscriptionName);
            return;
        }

        _processor = _client.CreateProcessor(_config.TopicName, _config.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = _config.MaxConcurrentCalls,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromSeconds(_config.MaxAutoLockRenewalSeconds),
                PrefetchCount = 0
            });

        _processor.ProcessMessageAsync += OnMessageReceivedAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        await _processor.StartProcessingAsync(cancellationToken);
        _isListening = true;

        _logger.LogInformation(
            "Started listening on topic {Topic}, subscription {Subscription} (MaxConcurrentCalls={MaxConcurrent})",
            _config.TopicName, _config.SubscriptionName, _config.MaxConcurrentCalls);
    }

    public async Task StopListeningAsync(CancellationToken cancellationToken = default)
    {
        if (_processor is not null && _isListening)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            _isListening = false;

            _logger.LogInformation("Stopped listening on topic {Topic}, subscription {Subscription}",
                _config.TopicName, _config.SubscriptionName);
        }
    }

    private async Task OnMessageReceivedAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        var messageType = args.Message.ApplicationProperties.TryGetValue("messageType", out var mt)
            ? mt?.ToString() ?? "Unknown"
            : "Unknown";
        var source = args.Message.ApplicationProperties.TryGetValue("source", out var src)
            ? src?.ToString() ?? "Unknown"
            : "Unknown";

        var properties = new Dictionary<string, string>();
        foreach (var prop in args.Message.ApplicationProperties)
        {
            properties[prop.Key] = prop.Value?.ToString() ?? string.Empty;
        }

        _logger.LogInformation(
            "Received message {MessageId} | Type: {MessageType} | Source: {Source} | " +
            "Enqueued: {EnqueuedTime} | DeliveryCount: {DeliveryCount} | Subscription: {Subscription}",
            args.Message.MessageId, messageType, source,
            args.Message.EnqueuedTime, args.Message.DeliveryCount, _config.SubscriptionName);

        _logger.LogDebug("Message body: {Body}", body);

        try
        {
            await _messageHandler.HandleMessageAsync(body, messageType, source, properties, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);

            _logger.LogInformation("Successfully processed and completed message {MessageId}", args.Message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}. Message will be retried.",
                args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private async Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus error on {Topic}/{Subscription} | Source: {ErrorSource} | Namespace: {Namespace} | EntityPath: {EntityPath}",
            _config.TopicName, _config.SubscriptionName,
            args.ErrorSource, args.FullyQualifiedNamespace, args.EntityPath);

        await _messageHandler.HandleErrorAsync(args.Exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
