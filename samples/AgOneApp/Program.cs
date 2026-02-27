// ─── AG One Product ───
// Just copy ServiceBusService.cs into your project and use it like this.

var connectionString = "<YOUR_SERVICE_BUS_CONNECTION_STRING>";
var topicName = "central-events";
var subscriptionName = "agone-subscription";

await using var serviceBus = new ServiceBusService(connectionString, topicName, subscriptionName);

// 1) LISTEN
await serviceBus.ListenAsync(
    onMessageReceived: async (message) =>
    {
        Console.WriteLine($"[AG One] Got message: {message}");
        await Task.CompletedTask;
    }
);

// 2) PUBLISH
var notification = new { UserId = "user-42", Event = "LoginCompleted", Timestamp = DateTime.UtcNow };
await serviceBus.PublishAsync(notification);

Console.WriteLine("Press any key to stop...");
Console.ReadKey();

await serviceBus.StopListeningAsync();
