using TalentSync.Ingress.Api;
using TalentSync.Ingress.Api.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIngress(builder.Configuration);

var app = builder.Build();

app.MapTeamtailorWebhook();
app.MapHealthChecks("/health/live");

app.Run();

/// <summary>Public so integration tests can host it with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
