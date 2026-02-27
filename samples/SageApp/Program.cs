using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

await using var serviceBus = new ServiceBusService(config);

// LISTEN
await serviceBus.ListenAsync(async (message) =>
{
    Console.WriteLine($"[Sage] Got message: {message}");
    await Task.CompletedTask;
});

// PUBLISH
var order = new { OrderId = 101, Customer = "Acme Corp", Amount = 2500.00 };
await serviceBus.PublishAsync(order);

Console.WriteLine("Press any key to stop...");
Console.ReadKey();
await serviceBus.StopListeningAsync();
