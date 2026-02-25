using GraphEmailService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder => builder.AddConsole());
services.AddGraphEmailService();

var provider = services.BuildServiceProvider();
var emailService = provider.GetRequiredService<IGraphEmailService>();

var success = await emailService.SendEmailAsync(
    toEmail: "recipient@example.com",
    toName: "Recipient Name",
    subject: "Test Email from Graph API",
    htmlContent: "<h1>Hello!</h1><p>This is a test email sent via Microsoft Graph.</p>");

Console.WriteLine(success ? "Email sent successfully." : "Email sending failed.");
