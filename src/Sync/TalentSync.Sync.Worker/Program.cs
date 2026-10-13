using TalentSync.Sync.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSyncWorker(builder.Configuration);

var host = builder.Build();
host.Run();
