using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TalentSync.Matching.Evaluation;

// No default provider: the user-secrets shared with Matching.Worker (for the API key) may set Llm:Provider = Anthropic,
// and a default taken from there would turn a smoke test into billed calls.
if (!args.Any(a => a == "--provider" || a.StartsWith("--provider=", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("Usage: --provider Fake|Anthropic [--runs N] [--out <dir>] [--<Section>:<Key> <value>]");
    return 2;
}

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
