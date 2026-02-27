using Central.ServiceBus.Models;

namespace Central.ServiceBus.Interfaces;

public interface IServiceBusSubscriber
{
    Task StartListeningAsync(CancellationToken cancellationToken = default);
    Task StopListeningAsync(CancellationToken cancellationToken = default);
    bool IsListening { get; }
}

/// <summary>
/// Implement this interface in your product to handle incoming messages.
/// Register it in DI and the subscriber will invoke it automatically.
/// </summary>
public interface IMessageHandler
{
    Task HandleMessageAsync(string messageBody, string messageType, string source,
        IDictionary<string, string> properties, CancellationToken cancellationToken = default);

    Task HandleErrorAsync(Exception exception, CancellationToken cancellationToken = default);
}
