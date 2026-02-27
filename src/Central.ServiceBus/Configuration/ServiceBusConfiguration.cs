namespace Central.ServiceBus.Configuration;

public class ServiceBusConfiguration
{
    public const string SectionName = "AzureServiceBus";

    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName { get; set; } = string.Empty;

    /// <summary>
    /// Each product (Sage, AgOne, AgOneWork) should set its own unique subscription name
    /// so it receives messages independently.
    /// </summary>
    public string SubscriptionName { get; set; } = string.Empty;

    public int MaxConcurrentCalls { get; set; } = 1;

    public int MaxAutoLockRenewalSeconds { get; set; } = 300;

    /// <summary>
    /// When true, the background subscriber will start automatically on application startup.
    /// </summary>
    public bool AutoStartSubscriber { get; set; } = true;
}
