using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

await using var serviceBus = new ServiceBusService(config);

// LISTEN
await serviceBus.ListenAsync(async (message) =>
{
    Console.WriteLine($"[AG One Work] Got message: {message}");
    await Task.CompletedTask;
});

// PUBLISH
var taskUpdate = new { TaskId = "T-999", Status = "Completed", AssignedTo = "john@company.com" };
await serviceBus.PublishAsync(taskUpdate);

Console.WriteLine("Press any key to stop...");
Console.ReadKey();
await serviceBus.StopListeningAsync();
