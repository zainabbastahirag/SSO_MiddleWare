using Central.ServiceBus.Models;

namespace Central.ServiceBus.Interfaces;

public interface IServiceBusPublisher
{
    Task PublishAsync<T>(T payload, string source, string? correlationId = null,
        Dictionary<string, string>? properties = null, CancellationToken cancellationToken = default) where T : class;

    Task PublishAsync<T>(ServiceBusMessageWrapper<T> message,
        CancellationToken cancellationToken = default) where T : class;

    Task PublishBatchAsync<T>(IEnumerable<T> payloads, string source, string? correlationId = null,
        CancellationToken cancellationToken = default) where T : class;
}
