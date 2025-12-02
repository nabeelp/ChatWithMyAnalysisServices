using ChatWithMyAnalysisServices.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

// Configuration
var chatConfig = new ChatConfiguration
{
    AasServer = builder.Configuration["AnalysisServices:Server"] ?? "",
    AasDatabase = builder.Configuration["AnalysisServices:Database"] ?? "",
    OpenAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"] ?? "",
    OpenAiKey = builder.Configuration["AzureOpenAI:ApiKey"] ?? "",
    OpenAiDeployment = builder.Configuration["AzureOpenAI:DeploymentName"] ?? ""
};
builder.Services.AddSingleton(chatConfig);
builder.Services.AddScoped<AnalysisServicesChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapHub<ChatWithMyAnalysisServices.Web.Hubs.ChatHub>("/chatHub");

app.Run();
