using TalentSync.Ingress.Api;
using TalentSync.Ingress.Api.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIngress(builder.Configuration);

var app = builder.Build();

app.MapTeamtailorWebhook();

app.Run();
