// ─── AG One Work Product ───
// Just copy ServiceBusService.cs into your project and use it like this.

var connectionString = "<YOUR_SERVICE_BUS_CONNECTION_STRING>";
var topicName = "central-events";
var subscriptionName = "agonework-subscription";

await using var serviceBus = new ServiceBusService(connectionString, topicName, subscriptionName);

// 1) LISTEN
await serviceBus.ListenAsync(
    onMessageReceived: async (message) =>
    {
        Console.WriteLine($"[AG One Work] Got message: {message}");
        await Task.CompletedTask;
    }
);

// 2) PUBLISH
var taskUpdate = new { TaskId = "T-999", Status = "Completed", AssignedTo = "john@company.com" };
await serviceBus.PublishAsync(taskUpdate);

Console.WriteLine("Press any key to stop...");
Console.ReadKey();

await serviceBus.StopListeningAsync();
