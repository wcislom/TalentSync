using Microsoft.Extensions.Options;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Evaluation.Cases;
using TalentSync.Matching.Evaluation.Metrics;
using TalentSync.Matching.Evaluation.Reporting;
using TalentSync.Matching.Infrastructure.Llm;
using TalentSync.Matching.Infrastructure.Llm.Fake;

namespace TalentSync.Matching.Evaluation;

/// <summary>Runs every case through <see cref="IEnrichmentService"/>, the production path (ADR 009), and writes the report.</summary>
internal sealed class EvaluationRunner(
    IEnrichmentService enrichmentService,
    IOptions<EvaluationOptions> evaluationOptions,
    IOptions<LlmOptions> llmOptions,
    TimeProvider timeProvider)
{
    /// <returns>0 when the report is complete and every metric meets its threshold, 1 otherwise.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var options = evaluationOptions.Value;
        var llm = llmOptions.Value;
        var casesDirectory = RepositoryRoot.Resolve(options.CasesDirectory);
        var cases = CaseLoader.Load(casesDirectory);
        if (cases.Count == 0)
        {
            throw new InvalidOperationException($"No case files in {casesDirectory}.");
        }

        var goldenSetHash = GoldenSetHash.Compute(casesDirectory);
        Console.WriteLine($"Prompt {enrichmentService.PromptVersion}, golden set {goldenSetHash}, {cases.Count} case(s) x {options.RunsPerCase} run(s), provider {llm.Provider}.");

        // Sequential on purpose: the shared circuit breaker then sees calls in the same order as in the worker.
        var results = new List<CaseResult>();
        foreach (var evaluationCase in cases)
        {
            var runs = new List<EnrichmentOutcome>();
            for (var run = 1; run <= options.RunsPerCase; run++)
            {
                var outcome = await enrichmentService.EnrichAsync(evaluationCase.Cv, cancellationToken);
                runs.Add(outcome);
                Console.WriteLine($"  {evaluationCase.Id} {run}/{options.RunsPerCase}: {Describe(outcome)}");
            }

            results.Add(new CaseResult(evaluationCase, runs, MetricCalculator.ForCase(evaluationCase, runs)));
        }

        var aggregate = MetricCalculator.Aggregate(results.Select(r => r.Metrics).ToList(), options.Thresholds);
        var header = new ReportHeader(
            enrichmentService.PromptVersion, goldenSetHash, ModelId(results, llm), llm.Provider!, options.RunsPerCase, timeProvider.GetUtcNow());

        var outputDirectory = RepositoryRoot.Resolve(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, MarkdownReport.FileName(header));
        // A rerun on the same day with the same prompt and model replaces the earlier report; git keeps committed ones.
        await File.WriteAllTextAsync(path, MarkdownReport.Render(header, aggregate, results), cancellationToken);

        Console.WriteLine($"{(aggregate.Incomplete ? "INCOMPLETE" : "COMPLETE")}, {(aggregate.Passed ? "PASS" : "FAIL")}: {path}");
        return aggregate.Passed ? 0 : 1;
    }

    private static string Describe(EnrichmentOutcome outcome) => outcome switch
    {
        EnrichmentOutcome.Enriched e => $"Enriched, {e.Enrichment.Seniority}, {e.Enrichment.Skills.Count} skill(s)",
        EnrichmentOutcome.Failed f => f.Reason.ToString(),
        _ => throw new InvalidOperationException("Unknown enrichment outcome."),
    };

    // The model that answered, as the provider reports it; the configured one only when no call succeeded.
    private static string ModelId(IEnumerable<CaseResult> results, LlmOptions llm)
    {
        var answered = results.SelectMany(r => r.Runs).OfType<EnrichmentOutcome.Enriched>()
            .Select(e => e.ModelId).Distinct(StringComparer.Ordinal).ToList();
        if (answered.Count > 0)
        {
            return string.Join(",", answered);
        }

        return llm.Provider == LlmOptions.FakeProvider ? FakeChatClient.ModelId : llm.Anthropic.Model ?? "unknown";
    }
}
