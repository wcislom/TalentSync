using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;
using TalentSync.Matching.Evaluation.Cases;
using TalentSync.Matching.Evaluation.Metrics;
using TalentSync.Matching.Evaluation.Reporting;

namespace TalentSync.Matching.Evaluation.Tests;

public sealed class MarkdownReportTests
{
    private static readonly ReportHeader Header = new(
        "0123456789abcdef", "fedcba9876543210", "claude-haiku-5-5", "Anthropic", 1, new DateTimeOffset(2026, 10, 15, 23, 30, 0, TimeSpan.FromHours(-2)));

    [Fact]
    public void FileName_UsesTheUtcDate_AndASanitisedModelId() =>
        Assert.Equal("2026-10-16-0123456789abcdef-vendor-model-1.md", MarkdownReport.FileName(Header with { ModelId = "vendor/model:1" }));

    [Fact]
    public void Render_WritesTheHeaderLines_AndEveryRawAnswer()
    {
        var evaluationCase = new EvaluationCase("c", "p", "cv", new ExpectedEnrichment([["C#"]], [], [], Seniority.Senior, 0));
        EnrichmentOutcome[] runs =
        [
            new EnrichmentOutcome.Enriched(new Enrichment([new Skill("C#"), new Skill("A|B")], Seniority.Mid, new Confidence(0.65)), "m"),
            new EnrichmentOutcome.Failed(EnrichmentFailure.Timeout),
        ];
        var metrics = MetricCalculator.ForCase(evaluationCase, runs);

        var report = MarkdownReport.Render(Header, MetricCalculator.Aggregate([metrics], new MetricThresholds()), [new CaseResult(evaluationCase, runs, metrics)]);

        Assert.Contains("- PromptVersion: 0123456789abcdef\n", report.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("- GoldenSetHash: fedcba9876543210\n", report.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("- ModelId: claude-haiku-5-5\n", report.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("- Status: INCOMPLETE", report, StringComparison.Ordinal);
        Assert.Contains("| 1 | Enriched | Mid | 0.65 | `C#` `A\\|B` |", report, StringComparison.Ordinal);
        Assert.Contains("| 2 | Timeout | | | |", report, StringComparison.Ordinal);
    }
}
