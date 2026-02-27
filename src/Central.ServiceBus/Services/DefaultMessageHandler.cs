using Central.ServiceBus.Interfaces;
using Microsoft.Extensions.Logging;

namespace Central.ServiceBus.Services;

/// <summary>
/// Default handler that logs received messages to the console/logger.
/// Products should register their own IMessageHandler to override this behavior.
/// </summary>
public class DefaultMessageHandler : IMessageHandler
{
    private readonly ILogger<DefaultMessageHandler> _logger;

    public DefaultMessageHandler(ILogger<DefaultMessageHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleMessageAsync(string messageBody, string messageType, string source,
        IDictionary<string, string> properties, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== Message Received ===");
        _logger.LogInformation("  Type   : {MessageType}", messageType);
        _logger.LogInformation("  Source : {Source}", source);
        _logger.LogInformation("  Body   : {Body}", messageBody);

        if (properties.Count > 0)
        {
            _logger.LogInformation("  Properties:");
            foreach (var prop in properties)
            {
                _logger.LogInformation("    {Key} = {Value}", prop.Key, prop.Value);
            }
        }

        _logger.LogInformation("========================");
        return Task.CompletedTask;
    }

    public Task HandleErrorAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        _logger.LogError(exception, "ServiceBus processing error");
        return Task.CompletedTask;
    }
}
