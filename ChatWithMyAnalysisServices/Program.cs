using System.Data;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using Spectre.Console;

namespace ChatWithMyAnalysisServices;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. Load Configuration
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var aasServer = config["AnalysisServices:Server"];
        var aasDatabase = config["AnalysisServices:Database"];
        var openAiEndpoint = config["AzureOpenAI:Endpoint"];
        var openAiKey = config["AzureOpenAI:ApiKey"];
        var openAiDeployment = config["AzureOpenAI:DeploymentName"];

        if (string.IsNullOrEmpty(aasServer) || string.IsNullOrEmpty(aasDatabase) || 
            string.IsNullOrEmpty(openAiEndpoint) || string.IsNullOrEmpty(openAiKey) || string.IsNullOrEmpty(openAiDeployment))
        {
            AnsiConsole.MarkupLine("[red]Error: Missing configuration in appsettings.json[/]");
            return;
        }

        AnsiConsole.Write(new FigletText("Chat With AAS").Color(Color.Blue));

        string? schemaContext = null;
        string? accessToken = null;
        DateTimeOffset tokenExpiration = DateTimeOffset.MinValue;

        // 2. Authenticate and Connect
        await AnsiConsole.Status()
            .StartAsync("Authenticating and fetching schema...", async ctx =>
            {
                try
                {
                    // Construct Scope from Server URI
                    // Server URI format: asazure://<region>.asazure.windows.net/<servername>
                    // We need scope: https://<region>.asazure.windows.net/.default
                    var serverUri = new Uri(aasServer);
                    var scope = $"https://{serverUri.Host}/.default";

                    AnsiConsole.MarkupLine($"[gray]Log: Requesting token for scope: {scope}[/]");
                    // Get Access Token for Analysis Services
                    var credential = new DefaultAzureCredential();
                    var tokenRequestContext = new TokenRequestContext(new[] { scope });
                    var tokenResult = await credential.GetTokenAsync(tokenRequestContext);
                    accessToken = tokenResult.Token;
                    tokenExpiration = tokenResult.ExpiresOn;
                    AnsiConsole.MarkupLine($"[gray]Log: Token received. Expires: {tokenExpiration}[/]");

                    ctx.Status("Connected to Azure AD. Fetching AAS Schema...");

                    // Connect to AAS
                    // Note: Provider=MSOLAP is not required/recommended for .NET Core client
                    var connectionString = $"Data Source={aasServer};Initial Catalog={aasDatabase};";
                    AnsiConsole.MarkupLine($"[gray]Log: Connecting with string: {connectionString}[/]");
                    
                    using var connection = new AdomdConnection(connectionString);
                    connection.AccessToken = new Microsoft.AnalysisServices.AccessToken(accessToken, tokenExpiration); // Use 2-arg constructor
                    
                    AnsiConsole.MarkupLine("[gray]Log: Opening ADOMD connection...[/]");
                    connection.Open();
                    AnsiConsole.MarkupLine("[gray]Log: Connection opened successfully.[/]");

                    // Fetch Schema (Tables and Columns)
                    schemaContext = GetSchemaContext(connection);
                    
                    ctx.Status("Schema fetched successfully.");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Connection Error: {Markup.Escape(ex.Message)}[/]");
                }
            });

        if (schemaContext == null || accessToken == null) return;

        // 3. Initialize OpenAI
        var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), new System.ClientModel.ApiKeyCredential(openAiKey));
        var chatClient = openAiClient.GetChatClient(openAiDeployment);

        AnsiConsole.MarkupLine("[green]Ready! Ask questions about your data (type 'exit' to quit).[/]");

        // 4. Chat Loop
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
                        // Generate DAX
                        var daxQuery = await GenerateDaxAsync(chatClient, userInput, schemaContext);
                        AnsiConsole.MarkupLine($"[gray]Log: Generated DAX: {Markup.Escape(daxQuery)}[/]");
                        
                        ctx.Status("Executing DAX...");
                        
                        // Execute DAX
                        var table = ExecuteDax(daxQuery, aasServer, aasDatabase, accessToken, tokenExpiration);
                        AnsiConsole.MarkupLine("[gray]Log: DAX executed successfully. Displaying results...[/]");

                        // Display Result
                        AnsiConsole.Write(table);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                    }
                });
        }
    }

    static string GetSchemaContext(AdomdConnection connection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Here is the schema of the Analysis Services model:");

        // 1. Get All Tables
        AnsiConsole.MarkupLine("[gray]Log: Fetching tables...[/]");
        var tables = new List<(string Id, string Name)>();
        try 
        {
            var tablesSet = connection.GetSchemaDataSet("TMSCHEMA_TABLES", null);
            if (tablesSet.Tables.Count > 0)
            {
                foreach (DataRow row in tablesSet.Tables[0].Rows)
                {
                    tables.Add((row["ID"]?.ToString() ?? "", row["Name"]?.ToString() ?? ""));
                }
            }
            AnsiConsole.MarkupLine($"[gray]Log: Found {tables.Count} tables.[/]");
        }
        catch (Exception ex)
        {
             AnsiConsole.MarkupLine($"[red]Error fetching tables: {Markup.Escape(ex.Message)}[/]");
             throw;
        }

        // 2. Get All Columns
        AnsiConsole.MarkupLine("[gray]Log: Fetching columns...[/]");
        var columns = new List<(string TableId, string Name)>();
        try
        {
            var columnsSet = connection.GetSchemaDataSet("TMSCHEMA_COLUMNS", null);
            if (columnsSet.Tables.Count > 0)
            {
                var dt = columnsSet.Tables[0];
                
                // Debug: Log available columns
                var availableCols = new List<string>();
                foreach (DataColumn col in dt.Columns) availableCols.Add(col.ColumnName);
                AnsiConsole.MarkupLine($"[gray]Log: Available columns in TMSCHEMA_COLUMNS: {string.Join(", ", availableCols)}[/]");

                foreach (DataRow row in dt.Rows)
                {
                    // Handle potential column name mismatches
                    string name = dt.Columns.Contains("Name") ? row["Name"]?.ToString() ?? "" : 
                                  dt.Columns.Contains("ExplicitName") ? row["ExplicitName"]?.ToString() ?? "" : "Unknown";
                    
                    string tableId = dt.Columns.Contains("TableID") ? row["TableID"]?.ToString() ?? "" : "";

                    columns.Add((tableId, name));
                }
            }
            AnsiConsole.MarkupLine($"[gray]Log: Found {columns.Count} columns.[/]");
        }
        catch (Exception ex)
        {
             AnsiConsole.MarkupLine($"[red]Error fetching columns: {Markup.Escape(ex.Message)}[/]");
             throw;
        }

        // 3. Build Context
        foreach (var table in tables)
        {
            if (string.IsNullOrEmpty(table.Name)) continue;

            sb.AppendLine($"Table: {table.Name}");
            sb.AppendLine("Columns:");
            
            foreach (var col in columns)
            {
                if (col.TableId == table.Id)
                {
                    sb.AppendLine($"- {col.Name}");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static async Task<string> GenerateDaxAsync(ChatClient client, string userInput, string schemaContext)
    {
        var systemPrompt = $@"You are an expert in DAX (Data Analysis Expressions) for Azure Analysis Services.
Your goal is to translate natural language questions into valid DAX queries.
Return ONLY the DAX query. Do not include markdown formatting (like ```dax ... ```). Do not include explanations.

IMPORTANT RULES:
1. ALWAYS enclose table names in single quotes, especially if they contain spaces (e.g. 'Internet Sales').
2. ALWAYS enclose column names in square brackets (e.g. [Sales Amount]).
3. Use fully qualified column names ('Table'[Column]) for all references.
4. When using SUMMARIZECOLUMNS, always place FILTER tables BEFORE the name/expression pairs.
   Syntax: SUMMARIZECOLUMNS(GroupByCols..., FilterTables..., ""Name"", Expression...)

{schemaContext}

Example:
User: Total sales by year
DAX: EVALUATE SUMMARIZECOLUMNS('Date'[Year], ""Total Sales"", SUM('Internet Sales'[Sales Amount]))

Example with Filter:
User: Sales for Ontario
DAX: EVALUATE SUMMARIZECOLUMNS('Geography'[State Province Name], FILTER('Geography', 'Geography'[State Province Name] = ""Ontario""), ""Total Sales"", SUM('Internet Sales'[Sales Amount]))
";

        ChatCompletion completion = await client.CompleteChatAsync(
            new List<ChatMessage>()
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userInput)
            });

        var response = completion.Content[0].Text.Trim();
        
        // Cleanup if the model adds markdown code blocks despite instructions
        if (response.StartsWith("```dax")) response = response.Substring(6);
        if (response.StartsWith("```")) response = response.Substring(3);
        if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);

        return response.Trim();
    }

    static Table ExecuteDax(string daxQuery, string? server, string? database, string accessToken, DateTimeOffset tokenExpiration)
    {
        var connectionString = $"Data Source={server};Initial Catalog={database};";
        using var connection = new AdomdConnection(connectionString);
        connection.AccessToken = new Microsoft.AnalysisServices.AccessToken(accessToken, tokenExpiration);
        connection.Open();

        using var command = new AdomdCommand(daxQuery, connection);
        using var reader = command.ExecuteReader();

        var table = new Table();
        
        // Add Columns
        for (int i = 0; i < reader.FieldCount; i++)
        {
            table.AddColumn(Markup.Escape(reader.GetName(i)));
        }

        // Add Rows
        while (reader.Read())
        {
            var rowValues = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i);
                rowValues.Add(Markup.Escape(val?.ToString() ?? ""));
            }
            table.AddRow(rowValues.ToArray());
        }

        return table;
    }
}
