using Central.ServiceBus.Extensions;
using Central.ServiceBus.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Register central ServiceBus with AG One Work-specific subscription
builder.Services.AddCentralServiceBus(builder.Configuration.GetSection("AzureServiceBus"));

// Example: register a custom handler specific to AG One Work
builder.Services.AddServiceBusMessageHandler<AgOneWorkMessageHandler>();

var host = builder.Build();

// Demo: publish a message from AG One Work
_ = Task.Run(async () =>
{
    await Task.Delay(3000);

    var publisher = host.Services.GetRequiredService<IServiceBusPublisher>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    var taskUpdate = new { TaskId = "T-999", Status = "Completed", AssignedTo = "john.doe@company.com" };
    await publisher.PublishAsync(taskUpdate, source: "AgOneWork", correlationId: Guid.NewGuid().ToString());

    logger.LogInformation("AG One Work published a task update message. Waiting for subscriber to process...");
});

await host.RunAsync();

/// <summary>
/// Custom message handler demonstrating product-specific processing logic.
/// </summary>
public class AgOneWorkMessageHandler : IMessageHandler
{
    private readonly ILogger<AgOneWorkMessageHandler> _logger;

    public AgOneWorkMessageHandler(ILogger<AgOneWorkMessageHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleMessageAsync(string messageBody, string messageType, string source,
        IDictionary<string, string> properties, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[AG One Work Handler] Processing message from {Source}", source);
        _logger.LogInformation("[AG One Work Handler] Type: {Type}", messageType);
        _logger.LogInformation("[AG One Work Handler] Body: {Body}", messageBody);

        // Add your custom AG One Work processing logic here
        // e.g., update a database, trigger a workflow, etc.

        return Task.CompletedTask;
    }

    public Task HandleErrorAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        _logger.LogError(exception, "[AG One Work Handler] Error processing message");
        return Task.CompletedTask;
    }
}
