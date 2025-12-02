using System.Data;
using ChatWithMyAnalysisServices.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChatWithMyAnalysisServices.Web.Pages;

public class IndexModel : PageModel
{
    private readonly AnalysisServicesChatService _chatService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(AnalysisServicesChatService chatService, ILogger<IndexModel> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    public void OnGet()
    {
    }
}
