using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;

namespace GraphEmailService;

public class GraphEmailService : IGraphEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphEmailService> _logger;

    public GraphEmailService(IConfiguration configuration, ILogger<GraphEmailService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string? toName,
        string subject,
        string htmlContent,
        List<string>? ccRecipients = null,
        Dictionary<string, string>? additionalHeaders = null,
        string? attachmentPath = null)
    {
        try
        {
            var graph = CreateGraphClientApp();
            var sender = _configuration["GraphApi:Sender"];

            if (string.IsNullOrWhiteSpace(sender))
                throw new InvalidOperationException("GraphApi:Sender is not configured.");

            var recipients = new List<string> { toEmail };

            await SendEmailCoreAsync(
                service: graph,
                recipient: recipients,
                subject: subject,
                body: htmlContent,
                ccRecipients: ccRecipients,
                additionalHeaders: additionalHeaders,
                attachmentPath: attachmentPath,
                senderMailbox: sender);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendEmailAsync failed for {Recipient}", toEmail);
            return false;
        }
    }

    private async Task SendEmailCoreAsync(
        GraphServiceClient service,
        List<string> recipient,
        string subject,
        string body,
        List<string>? ccRecipients = null,
        Dictionary<string, string>? additionalHeaders = null,
        string? attachmentPath = null,
        string? senderMailbox = null)
    {
        if (recipient == null || recipient.Count == 0)
        {
            _logger.LogWarning("No recipients provided.");
            return;
        }

        var email = new Message
        {
            Subject = subject,
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = body
            },
            ToRecipients = BuildRecipientList(recipient)
        };

        if (ccRecipients is { Count: > 0 })
        {
            email.CcRecipients = BuildRecipientList(ccRecipients);
        }

        if (additionalHeaders is { Count: > 0 })
        {
            email.InternetMessageHeaders = additionalHeaders
                .Select(kv => new InternetMessageHeader { Name = kv.Key, Value = kv.Value })
                .ToList();
        }

        if (!string.IsNullOrEmpty(attachmentPath))
        {
            if (!File.Exists(attachmentPath))
            {
                _logger.LogWarning("Attachment file not found: {Path}", attachmentPath);
                return;
            }

            var fileBytes = await File.ReadAllBytesAsync(attachmentPath);
            email.Attachments = new List<Attachment>
            {
                new FileAttachment
                {
                    Name = Path.GetFileName(attachmentPath),
                    ContentBytes = fileBytes
                }
            };
        }

        if (string.IsNullOrWhiteSpace(senderMailbox))
            throw new InvalidOperationException("Sender mailbox is required for app-only flow.");

        try
        {
            var sendMailBody = new SendMailPostRequestBody
            {
                Message = email,
                SaveToSentItems = true
            };

            await service.Users[senderMailbox].SendMail.PostAsync(sendMailBody);

            _logger.LogInformation(
                "Email sent via /users/{Sender}/sendMail to {To}",
                senderMailbox,
                string.Join(", ", recipient));
        }
        catch (ODataError odataError)
        {
            _logger.LogError(
                odataError,
                "Graph API error sending mail. Code={Code}, Message={Message}",
                odataError.Error?.Code,
                odataError.Error?.Message);
            throw;
        }
    }

    private GraphServiceClient CreateGraphClientApp()
    {
        var tenantId = _configuration["GraphApi:TenantId"];
        var clientId = _configuration["GraphApi:ClientId"];
        var clientSecret = _configuration["GraphApi:ClientSecret"];

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "GraphApi settings are missing TenantId, ClientId, or ClientSecret.");
        }

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        return new GraphServiceClient(credential, scopes);
    }

    private static List<Recipient> BuildRecipientList(IEnumerable<string> addresses)
    {
        return addresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => new Recipient
            {
                EmailAddress = new EmailAddress { Address = a.Trim() }
            })
            .ToList();
    }
}
