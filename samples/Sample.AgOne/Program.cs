using Central.ServiceBus.Extensions;
using Central.ServiceBus.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Register central ServiceBus with AG One-specific subscription
builder.Services.AddCentralServiceBus(builder.Configuration.GetSection("AzureServiceBus"));

var host = builder.Build();

// Demo: publish a message from AG One
_ = Task.Run(async () =>
{
    await Task.Delay(3000);

    var publisher = host.Services.GetRequiredService<IServiceBusPublisher>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    var notification = new { UserId = "user-42", Event = "LoginCompleted", Timestamp = DateTime.UtcNow };
    await publisher.PublishAsync(notification, source: "AgOne", correlationId: Guid.NewGuid().ToString());

    logger.LogInformation("AG One published a notification message. Waiting for subscriber to process...");
});

await host.RunAsync();
