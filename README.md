# Microsoft Graph Email Service

A C# library for sending emails via Microsoft Graph API using app-only (client credentials) authentication with Azure AD.

## Prerequisites

1. An Azure AD App Registration with:
   - **Application (not delegated) permission**: `Mail.Send`
   - Admin consent granted for `Mail.Send`
2. A mailbox (shared or user) that the app will send from, e.g. `noreply@yourdomain.com`
3. .NET 8.0 SDK

## Azure AD Setup

1. Go to **Azure Portal** > **Azure Active Directory** > **App registrations** > **New registration**
2. Note the **Application (client) ID** and **Directory (tenant) ID**
3. Under **Certificates & secrets**, create a new **Client secret** and copy the value
4. Under **API permissions**:
   - Add **Microsoft Graph** > **Application permissions** > `Mail.Send`
   - Click **Grant admin consent**

## Configuration

Add the following to your `appsettings.json`:

```json
{
  "GraphApi": {
    "TenantId": "YOUR_TENANT_ID",
    "ClientId": "YOUR_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "Sender": "noreply@yourdomain.com"
  }
}
```

## Usage

### Register the service (DI)

```csharp
services.AddGraphEmailService();
```

### Send an email

```csharp
var success = await emailService.SendEmailAsync(
    toEmail: "recipient@example.com",
    toName: "Recipient Name",
    subject: "Hello from Graph",
    htmlContent: "<h1>Hi!</h1><p>This is a test.</p>",
    ccRecipients: new List<string> { "cc@example.com" },
    attachmentPath: "/path/to/file.pdf");
```

## Project Structure

```
src/
  GraphEmailService/            # Class library
    IGraphEmailService.cs       # Interface
    GraphEmailService.cs        # Implementation
    ServiceCollectionExtensions.cs  # DI registration helper
  GraphEmailService.Sample/     # Console app demo
    Program.cs
    appsettings.json
```

## Running the Sample

```bash
cd src/GraphEmailService.Sample
# Edit appsettings.json with your real credentials
dotnet run
```
