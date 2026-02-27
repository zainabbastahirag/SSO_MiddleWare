using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

await using var serviceBus = new ServiceBusService(config);

// LISTEN
await serviceBus.ListenAsync(async (message) =>
{
    Console.WriteLine($"[AG One] Got message: {message}");
    await Task.CompletedTask;
});

// PUBLISH
var notification = new { UserId = "user-42", Event = "LoginCompleted", Timestamp = DateTime.UtcNow };
await serviceBus.PublishAsync(notification);

Console.WriteLine("Press any key to stop...");
Console.ReadKey();
await serviceBus.StopListeningAsync();
