namespace GraphEmailService;

public interface IGraphEmailService
{
    Task<bool> SendEmailAsync(
        string toEmail,
        string? toName,
        string subject,
        string htmlContent,
        List<string>? ccRecipients = null,
        Dictionary<string, string>? additionalHeaders = null,
        string? attachmentPath = null);
}
