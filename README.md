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
