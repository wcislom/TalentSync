using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TalentSync.Matching.Evaluation;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });

// Added explicitly: the host adds user-secrets only in Development, and the key must not depend on DOTNET_ENVIRONMENT.
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
builder.Configuration.AddCommandLine(args, new Dictionary<string, string>
{
    ["--runs"] = "Evaluation:RunsPerCase",
    ["--provider"] = "Llm:Provider",
    ["--out"] = "Evaluation:OutputDirectory",
});

builder.Services.AddEvaluation(builder.Configuration);

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await host.Services.GetRequiredService<EvaluationRunner>().RunAsync(cancellation.Token);
