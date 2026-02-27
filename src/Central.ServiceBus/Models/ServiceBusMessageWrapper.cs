namespace Central.ServiceBus.Models;

public class ServiceBusMessageWrapper<T> where T : class
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string Source { get; set; } = string.Empty;
    public string MessageType { get; set; } = typeof(T).Name;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
    public T? Payload { get; set; }
}

public class ReceivedMessageContext<T> where T : class
{
    public ServiceBusMessageWrapper<T> Message { get; set; } = null!;
    public string SequenceNumber { get; set; } = string.Empty;
    public string LockToken { get; set; } = string.Empty;
    public DateTime EnqueuedTime { get; set; }
    public int DeliveryCount { get; set; }
}
