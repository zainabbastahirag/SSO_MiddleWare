using Central.ServiceBus.Configuration;
using Central.ServiceBus.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Central.ServiceBus.Services;

/// <summary>
/// Background service that automatically starts the subscriber on application startup.
/// </summary>
public class ServiceBusSubscriberHostedService : BackgroundService
{
    private readonly IServiceBusSubscriber _subscriber;
    private readonly ServiceBusConfiguration _config;
    private readonly ILogger<ServiceBusSubscriberHostedService> _logger;

    public ServiceBusSubscriberHostedService(
        IServiceBusSubscriber subscriber,
        IOptions<ServiceBusConfiguration> options,
        ILogger<ServiceBusSubscriberHostedService> logger)
    {
        _subscriber = subscriber;
        _config = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.AutoStartSubscriber)
        {
            _logger.LogInformation("AutoStartSubscriber is disabled. Subscriber will not start automatically.");
            return;
        }

        _logger.LogInformation("Starting ServiceBus subscriber for {Topic}/{Subscription}...",
            _config.TopicName, _config.SubscriptionName);

        await _subscriber.StartListeningAsync(stoppingToken);

        stoppingToken.Register(async () =>
        {
            _logger.LogInformation("Stopping ServiceBus subscriber...");
            await _subscriber.StopListeningAsync(CancellationToken.None);
        });
    }
}
