using TalentSync.Sync.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSyncWorker(builder.Configuration);

var app = builder.Build();

// Liveness only, no dependency checks: the handler loop waits out a SQL, CRM or Event Hubs outage. Failing liveness
// on a dependency would make an orchestrator restart every replica during the outage without fixing anything.
app.MapHealthChecks("/health/live");

app.Run();

/// <summary>Public so integration tests can host it with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
