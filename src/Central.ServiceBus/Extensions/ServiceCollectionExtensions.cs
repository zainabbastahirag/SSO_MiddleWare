using Azure.Messaging.ServiceBus;
using Central.ServiceBus.Configuration;
using Central.ServiceBus.Interfaces;
using Central.ServiceBus.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Central.ServiceBus.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the central ServiceBus publisher and subscriber services.
    /// Each product should call this in its Program.cs / Startup.cs with its own configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddCentralServiceBus(builder.Configuration.GetSection("AzureServiceBus"));
    /// </code>
    /// </example>
    public static IServiceCollection AddCentralServiceBus(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfigurationSection configSection)
    {
        services.Configure<ServiceBusConfiguration>(configSection);
        return services.AddCentralServiceBusCore();
    }

    /// <summary>
    /// Registers the central ServiceBus services with an action-based configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddCentralServiceBus(opts =>
    /// {
    ///     opts.ConnectionString = "Endpoint=sb://...";
    ///     opts.TopicName = "central-events";
    ///     opts.SubscriptionName = "sage-subscription";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCentralServiceBus(
        this IServiceCollection services,
        Action<ServiceBusConfiguration> configure)
    {
        services.Configure(configure);
        return services.AddCentralServiceBusCore();
    }

    private static IServiceCollection AddCentralServiceBusCore(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ServiceBusConfiguration>>().Value;
            if (string.IsNullOrWhiteSpace(config.ConnectionString))
                throw new InvalidOperationException(
                    "ServiceBus ConnectionString is not configured. " +
                    "Set it in appsettings.json under AzureServiceBus:ConnectionString.");

            return new ServiceBusClient(config.ConnectionString);
        });

        services.AddSingleton<IServiceBusPublisher, ServiceBusPublisher>();
        services.AddSingleton<IServiceBusSubscriber, ServiceBusSubscriber>();

        services.TryAddDefaultMessageHandler();

        services.AddHostedService<ServiceBusSubscriberHostedService>();

        return services;
    }

    /// <summary>
    /// Registers a custom message handler. Call this BEFORE AddCentralServiceBus
    /// to override the default handler, or call it after.
    /// </summary>
    public static IServiceCollection AddServiceBusMessageHandler<THandler>(this IServiceCollection services)
        where THandler : class, IMessageHandler
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageHandler));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<IMessageHandler, THandler>();
        return services;
    }

    private static void TryAddDefaultMessageHandler(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IMessageHandler)))
        {
            services.AddSingleton<IMessageHandler, DefaultMessageHandler>();
        }
    }
}
