// ─── Sage Product ───
// Just copy ServiceBusService.cs into your project and use it like this.

var connectionString = "<YOUR_SERVICE_BUS_CONNECTION_STRING>";
var topicName = "central-events";
var subscriptionName = "sage-subscription";

await using var serviceBus = new ServiceBusService(connectionString, topicName, subscriptionName);

// 1) LISTEN — starts receiving messages in the background
await serviceBus.ListenAsync(
    onMessageReceived: async (message) =>
    {
        Console.WriteLine($"[Sage] Got message: {message}");
        await Task.CompletedTask;
    },
    onError: async (ex) =>
    {
        Console.WriteLine($"[Sage] Error: {ex.Message}");
        await Task.CompletedTask;
    }
);

// 2) PUBLISH — send a message to the topic
var order = new { OrderId = 101, Customer = "Acme Corp", Amount = 2500.00 };
await serviceBus.PublishAsync(order);

Console.WriteLine("Press any key to stop...");
Console.ReadKey();

await serviceBus.StopListeningAsync();
