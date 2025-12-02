using System.Data;
using ChatWithMyAnalysisServices.Core;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace ChatWithMyAnalysisServices.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. Load Configuration
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            
        var configuration = configBuilder.Build();

        var chatConfig = new ChatConfiguration
        {
            AasServer = configuration["AnalysisServices:Server"] ?? "",
            AasDatabase = configuration["AnalysisServices:Database"] ?? "",
            OpenAiEndpoint = configuration["AzureOpenAI:Endpoint"] ?? "",
            OpenAiKey = configuration["AzureOpenAI:ApiKey"] ?? "",
            OpenAiDeployment = configuration["AzureOpenAI:DeploymentName"] ?? ""
        };

        if (string.IsNullOrEmpty(chatConfig.AasServer) || string.IsNullOrEmpty(chatConfig.AasDatabase) || 
            string.IsNullOrEmpty(chatConfig.OpenAiEndpoint) || string.IsNullOrEmpty(chatConfig.OpenAiKey) || string.IsNullOrEmpty(chatConfig.OpenAiDeployment))
        {
            AnsiConsole.MarkupLine("[red]Error: Missing configuration in appsettings.json[/]");
            return;
        }

        AnsiConsole.Write(new FigletText("Chat With AAS").Color(Color.Blue));

        var chatService = new AnalysisServicesChatService(chatConfig);

        // 2. Authenticate and Connect
        await AnsiConsole.Status()
            .StartAsync("Authenticating and fetching schema...", async ctx =>
            {
                try
                {
                    await chatService.InitializeAsync(log => AnsiConsole.MarkupLine($"[gray]Log: {Markup.Escape(log)}[/]"));
                    ctx.Status("Schema fetched successfully.");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Connection Error: {Markup.Escape(ex.Message)}[/]");
                    Environment.Exit(1);
                }
            });

        AnsiConsole.MarkupLine("[green]Ready! Ask questions about your data (type 'exit' to quit).[/]");

        // 3. Chat Loop
        while (true)
        {
            var userInput = AnsiConsole.Ask<string>("[bold yellow]You:[/]");
            if (userInput.ToLower() == "exit") break;

            await AnsiConsole.Status()
                .StartAsync("Thinking...", async ctx =>
                {
                    try
                    {
                        AnsiConsole.MarkupLine("[gray]Log: Generating DAX...[/]");
                        var daxQuery = await chatService.GenerateDaxAsync(userInput);
                        AnsiConsole.MarkupLine($"[gray]Log: Generated DAX: {Markup.Escape(daxQuery)}[/]");
                        
                        ctx.Status("Executing DAX...");
                        
                        var dataTable = chatService.ExecuteDax(daxQuery);
                        AnsiConsole.MarkupLine("[gray]Log: DAX executed successfully. Displaying results...[/]");

                        // Display Result
                        var table = new Table();
                        foreach (DataColumn col in dataTable.Columns)
                        {
                            table.AddColumn(Markup.Escape(col.ColumnName));
                        }

                        foreach (DataRow row in dataTable.Rows)
                        {
                            var rowValues = new List<string>();
                            foreach (var item in row.ItemArray)
                            {
                                rowValues.Add(Markup.Escape(item?.ToString() ?? ""));
                            }
                            table.AddRow(rowValues.ToArray());
                        }

                        AnsiConsole.Write(table);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                    }
                });
        }
    }
}
