using ChatWithMyAnalysisServices.Core;
using Microsoft.AspNetCore.SignalR;
using System.Data;

namespace ChatWithMyAnalysisServices.Web.Hubs;

public class ChatHub : Hub
{
    private readonly AnalysisServicesChatService _chatService;

    public ChatHub(AnalysisServicesChatService chatService)
    {
        _chatService = chatService;
    }

    public async Task ProcessRequest(string userInput)
    {
        try
        {
            await Clients.Caller.SendAsync("ReceiveLog", "Initializing service...");
            await _chatService.InitializeAsync(async log => await Clients.Caller.SendAsync("ReceiveLog", log));

            await Clients.Caller.SendAsync("ReceiveLog", "Generating DAX...");
            var daxQuery = await _chatService.GenerateDaxAsync(userInput);
            await Clients.Caller.SendAsync("ReceiveLog", $"Generated DAX: {daxQuery}");

            await Clients.Caller.SendAsync("ReceiveLog", "Executing DAX...");
            var resultTable = _chatService.ExecuteDax(daxQuery);
            await Clients.Caller.SendAsync("ReceiveLog", "Execution complete.");

            // Convert DataTable to a list of dictionaries for easier JSON serialization
            var result = new List<Dictionary<string, object>>();
            foreach (DataRow row in resultTable.Rows)
            {
                var dict = new Dictionary<string, object>();
                foreach (DataColumn col in resultTable.Columns)
                {
                    dict[col.ColumnName] = row[col];
                }
                result.Add(dict);
            }
            
            // Also send columns to help render the table
            var columns = new List<string>();
            foreach(DataColumn col in resultTable.Columns)
            {
                columns.Add(col.ColumnName);
            }

            await Clients.Caller.SendAsync("ReceiveResult", new { Columns = columns, Data = result });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
        }
    }
}
