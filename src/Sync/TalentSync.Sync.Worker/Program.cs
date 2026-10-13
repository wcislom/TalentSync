using TalentSync.Sync.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSyncWorker(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health/live");

app.Run();

public partial class Program;
