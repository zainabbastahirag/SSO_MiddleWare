using Central.ServiceBus.Extensions;
using Central.ServiceBus.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Register central ServiceBus with Sage-specific subscription
builder.Services.AddCentralServiceBus(builder.Configuration.GetSection("AzureServiceBus"));

// Optionally register a custom message handler (uncomment to override the default):
// builder.Services.AddServiceBusMessageHandler<SageMessageHandler>();

var host = builder.Build();

// Demo: publish a message from Sage, then let the subscriber pick it up
_ = Task.Run(async () =>
{
    await Task.Delay(3000);

    var publisher = host.Services.GetRequiredService<IServiceBusPublisher>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    var sampleOrder = new { OrderId = 101, Customer = "Acme Corp", Amount = 2500.00 };
    await publisher.PublishAsync(sampleOrder, source: "Sage", correlationId: Guid.NewGuid().ToString());

    logger.LogInformation("Sage published a sample order message. Waiting for subscriber to process...");
});

await host.RunAsync();
