using System.Data;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.AnalysisServices.AdomdClient;
using OpenAI.Chat;

namespace ChatWithMyAnalysisServices.Core;

public class AnalysisServicesChatService
{
    private readonly ChatConfiguration _config;
    private string? _accessToken;
    private DateTimeOffset _tokenExpiration;
    private string? _schemaContext;
    private ChatClient? _chatClient;

    public AnalysisServicesChatService(ChatConfiguration config)
    {
        _config = config;
    }

    public async Task InitializeAsync(Action<string>? logger = null)
    {
        logger?.Invoke($"Requesting token for scope: https://{new Uri(_config.AasServer).Host}/.default");
        
        var serverUri = new Uri(_config.AasServer);
        var scope = $"https://{serverUri.Host}/.default";
        var credential = new DefaultAzureCredential();
        var tokenRequestContext = new TokenRequestContext(new[] { scope });
        var tokenResult = await credential.GetTokenAsync(tokenRequestContext);
        _accessToken = tokenResult.Token;
        _tokenExpiration = tokenResult.ExpiresOn;
        
        logger?.Invoke($"Token received. Expires: {_tokenExpiration}");
        logger?.Invoke($"Connecting to AAS: {_config.AasServer}");

        var connectionString = $"Data Source={_config.AasServer};Initial Catalog={_config.AasDatabase};";
        using var connection = new AdomdConnection(connectionString);
        connection.AccessToken = new Microsoft.AnalysisServices.AccessToken(_accessToken, _tokenExpiration);
        connection.Open();
        
        logger?.Invoke("Connection opened. Fetching schema...");
        _schemaContext = GetSchemaContext(connection, logger);
        
        var openAiClient = new AzureOpenAIClient(new Uri(_config.OpenAiEndpoint), new System.ClientModel.ApiKeyCredential(_config.OpenAiKey));
        _chatClient = openAiClient.GetChatClient(_config.OpenAiDeployment);
        
        logger?.Invoke("Initialization complete.");
    }

    public async Task<string> GenerateDaxAsync(string userInput)
    {
        if (_chatClient == null || _schemaContext == null) throw new InvalidOperationException("Service not initialized.");

        string promptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts", "System.prompty");
        string rawPrompt = await File.ReadAllTextAsync(promptPath);
        
        string systemPrompt = rawPrompt;
        if (rawPrompt.StartsWith("---"))
        {
            int endOfFrontmatter = rawPrompt.IndexOf("---", 3);
            if (endOfFrontmatter != -1)
            {
                systemPrompt = rawPrompt.Substring(endOfFrontmatter + 3).Trim();
            }
        }

        if (systemPrompt.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
        {
            systemPrompt = systemPrompt.Substring(7).Trim();
        }

        systemPrompt = systemPrompt.Replace("{{schemaContext}}", _schemaContext);

        ChatCompletion completion = await _chatClient.CompleteChatAsync(
            new List<ChatMessage>()
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userInput)
            });

        var response = completion.Content[0].Text.Trim();
        
        if (response.StartsWith("```dax")) response = response.Substring(6);
        if (response.StartsWith("```")) response = response.Substring(3);
        if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);

        return response.Trim();
    }

    public DataTable ExecuteDax(string daxQuery)
    {
        if (_accessToken == null) throw new InvalidOperationException("Service not initialized.");

        var connectionString = $"Data Source={_config.AasServer};Initial Catalog={_config.AasDatabase};";
        using var connection = new AdomdConnection(connectionString);
        connection.AccessToken = new Microsoft.AnalysisServices.AccessToken(_accessToken, _tokenExpiration);
        connection.Open();

        using var command = new AdomdCommand(daxQuery, connection);
        using var adapter = new AdomdDataAdapter(command);
        var dataTable = new DataTable();
        adapter.Fill(dataTable);
        return dataTable;
    }

    private string GetSchemaContext(AdomdConnection connection, Action<string>? logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Here is the schema of the Analysis Services model:");

        logger?.Invoke("Fetching tables...");
        var tables = new List<(string Id, string Name)>();
        var tablesSet = connection.GetSchemaDataSet("TMSCHEMA_TABLES", null);
        if (tablesSet.Tables.Count > 0)
        {
            foreach (DataRow row in tablesSet.Tables[0].Rows)
            {
                tables.Add((row["ID"]?.ToString() ?? "", row["Name"]?.ToString() ?? ""));
            }
        }

        logger?.Invoke("Fetching columns...");
        var columns = new List<(string TableId, string Name)>();
        var columnsSet = connection.GetSchemaDataSet("TMSCHEMA_COLUMNS", null);
        if (columnsSet.Tables.Count > 0)
        {
            var dt = columnsSet.Tables[0];
            foreach (DataRow row in dt.Rows)
            {
                string name = dt.Columns.Contains("Name") ? row["Name"]?.ToString() ?? "" : 
                              dt.Columns.Contains("ExplicitName") ? row["ExplicitName"]?.ToString() ?? "" : "Unknown";
                string tableId = dt.Columns.Contains("TableID") ? row["TableID"]?.ToString() ?? "" : "";
                columns.Add((tableId, name));
            }
        }

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
}
