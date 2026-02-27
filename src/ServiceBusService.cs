using System.Text.Json;
using Azure.Messaging.ServiceBus;

/// <summary>
/// Single self-contained Azure Service Bus service.
/// Copy this file into any project (Sage, AG One, AG One Work).
/// Only requires NuGet: Azure.Messaging.ServiceBus
/// </summary>
public class ServiceBusService : IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private ServiceBusSender? _sender;
    private ServiceBusProcessor? _processor;

    public ServiceBusService(string connectionString, string topicName, string subscriptionName)
    {
        _client = new ServiceBusClient(connectionString);
        _topicName = topicName;
        _subscriptionName = subscriptionName;
    }

    // ──────────────────────────────────────────────
    //  PUBLISH  –  send a message to the topic
    // ──────────────────────────────────────────────

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        _sender ??= _client.CreateSender(_topicName);

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var sbMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json",
            Subject = typeof(T).Name
        };

        await _sender.SendMessageAsync(sbMessage, cancellationToken);

        Console.WriteLine($"[ServiceBus] Published to '{_topicName}': {json}");
    }

    // ──────────────────────────────────────────────
    //  LISTEN  –  subscribe and read messages
    // ──────────────────────────────────────────────

    public async Task ListenAsync(
        Func<string, Task> onMessageReceived,
        Func<Exception, Task>? onError = null,
        CancellationToken cancellationToken = default)
    {
        _processor = _client.CreateProcessor(_topicName, _subscriptionName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += async args =>
        {
            var body = args.Message.Body.ToString();

            Console.WriteLine($"[ServiceBus] Received on '{_subscriptionName}': {body}");

            try
            {
                await onMessageReceived(body);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceBus] Error processing message: {ex.Message}");
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
            }
        };

        _processor.ProcessErrorAsync += args =>
        {
            Console.WriteLine($"[ServiceBus] Error: {args.Exception.Message}");
            return onError?.Invoke(args.Exception) ?? Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(cancellationToken);

        Console.WriteLine($"[ServiceBus] Listening on '{_topicName}' / '{_subscriptionName}'...");
    }

    // ──────────────────────────────────────────────
    //  STOP + DISPOSE
    // ──────────────────────────────────────────────

    public async Task StopListeningAsync()
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync();
            Console.WriteLine("[ServiceBus] Stopped listening.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender is not null) await _sender.DisposeAsync();
        if (_processor is not null) await _processor.DisposeAsync();
        await _client.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
