using TalentSync.Matching.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMatchingWorker(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health/live");

app.Run();

public partial class Program;
