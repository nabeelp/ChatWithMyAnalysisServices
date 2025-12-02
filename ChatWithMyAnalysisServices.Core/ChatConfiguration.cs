namespace ChatWithMyAnalysisServices.Core;

public class ChatConfiguration
{
    public string AasServer { get; set; } = string.Empty;
    public string AasDatabase { get; set; } = string.Empty;
    public string OpenAiEndpoint { get; set; } = string.Empty;
    public string OpenAiKey { get; set; } = string.Empty;
    public string OpenAiDeployment { get; set; } = string.Empty;
}
