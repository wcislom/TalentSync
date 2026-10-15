using TalentSync.Matching.Evaluation.Cases;
using TalentSync.Matching.Evaluation.Reporting;
using TalentSync.Matching.Infrastructure.Llm;
using TalentSync.Matching.Infrastructure.Llm.Fake;

namespace TalentSync.Matching.Evaluation.Tests;

/// <summary>
/// CI guard of ADR 009: a change to the prompt or the golden set needs a report from a real model in the same PR.
/// It checks that such a report exists, not that a model produced it or that it passed its thresholds.
/// </summary>
public sealed class EvaluationReportGuardTests
{
    [Fact]
    public void CommittedReport_ExistsForTheCurrentPromptAndGoldenSet()
    {
        var reportsDirectory = RepositoryRoot.Resolve("eval/reports");
        var goldenSetHash = GoldenSetHash.Compute(RepositoryRoot.Resolve("eval/cases"));

        // Top level only: eval/reports/local/ holds exploratory runs that are never committed.
        var headers = Directory.Exists(reportsDirectory)
            ? Directory.GetFiles(reportsDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .Select(path => MarkdownReport.ReadHeader(File.ReadAllText(path)))
                .ToList()
            : [];

        var found = headers.Any(h =>
            h.GetValueOrDefault(MarkdownReport.PromptVersionKey) == EnrichmentPrompt.Version
            && h.GetValueOrDefault(MarkdownReport.GoldenSetHashKey) == goldenSetHash
            && h.TryGetValue(MarkdownReport.ModelIdKey, out var modelId)
            && modelId != FakeChatClient.ModelId);

        Assert.True(found,
            $"eval/reports/ has no report for PromptVersion {EnrichmentPrompt.Version} and GoldenSetHash {goldenSetHash} " +
            $"from a real model. Run eval/run-eval.sh with PROVIDER=\"Anthropic\" and OUT=\"eval/reports\", and commit the report (ADR 009).");
    }
}
