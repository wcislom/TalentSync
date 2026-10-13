using TalentSync.Matching.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMatchingWorker(builder.Configuration);

var host = builder.Build();
host.Run();
