# Chat With My Analysis Services

A .NET 8 application that enables natural language chat interactions with Azure Analysis Services using Azure OpenAI.

## Projects

### ChatWithMyAnalysisServices.Core
The core library containing the main chat service and configuration logic for interacting with Azure Analysis Services through AI-powered conversations.

### ChatWithMyAnalysisServices.Web
An ASP.NET Core web application providing a browser-based chat interface using SignalR for real-time communication.

### ChatWithMyAnalysisServices.CLI
A command-line interface for interacting with the chat service directly from the terminal.

## Prerequisites

- .NET 8 SDK
- Azure Analysis Services instance
- Azure OpenAI Service

## Configuration

Copy `appsettings.sample.json` to `appsettings.json` in the appropriate project folder and configure the required settings.

## Using Managed Identity with Azure Analysis Services

When deploying the web application to Azure (e.g., Azure App Service or Azure Container Apps), you can use a Managed Identity to securely connect to Azure Analysis Services without storing credentials in your configuration.

### Setup Steps

1. **Create or assign a Managed Identity** to your Azure web app (System-assigned or User-assigned).

2. **Add the Managed Identity as an Analysis Services Administrator**

   Azure Analysis Services does not natively support managed identities through the Azure Portal UI. You must use PowerShell to add the identity as a server administrator:

   ```powershell
   Set-AzAnalysisServicesServer -Name "<aas_server_name>" -ResourceGroupName "<rg_name>" -Administrator "app:<ManagedIdentityClientId>@<TenantId>"
   ```

   Replace:
   - `<aas_server_name>` - Your Azure Analysis Services server name
   - `<rg_name>` - The resource group containing your AAS instance
   - `<ManagedIdentityClientId>` - The Client ID of your Managed Identity
   - `<TenantId>` - Your Azure AD Tenant ID

3. **Verify the configuration** in the Azure Portal by navigating to your Analysis Services resource → Settings → Security → Server administrators. The managed identity should appear in the list.

### Notes

- Azure CLI does not currently support managing Azure Analysis Services administrators. PowerShell is required.
- For service principal authentication as an alternative, refer to the [Azure Analysis Services documentation](https://learn.microsoft.com/en-us/azure/analysis-services/analysis-services-server-admins).

## Getting Started

### Running the Web Application

```bash
dotnet run --project ChatWithMyAnalysisServices.Web
```

### Running the CLI

```bash
dotnet run --project ChatWithMyAnalysisServices.CLI
```

## License

This project is provided as-is.
