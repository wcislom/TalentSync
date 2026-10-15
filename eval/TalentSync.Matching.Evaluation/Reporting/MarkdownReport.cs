using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Evaluation.Cases;
using TalentSync.Matching.Evaluation.Metrics;

namespace TalentSync.Matching.Evaluation.Reporting;

public sealed record ReportHeader(
    string PromptVersion, string GoldenSetHash, string ModelId, string Provider, int RunsPerCase, DateTimeOffset GeneratedAt);

public sealed record CaseResult(EvaluationCase Case, IReadOnlyList<EnrichmentOutcome> Runs, CaseMetrics Metrics);

/// <summary>
/// The report of ADR 009. The header lines (<c>- Name: value</c>) are read back by the CI guard; the raw section holds
/// every answer, so every metric can be recomputed from it and the case files.
/// </summary>
public static partial class MarkdownReport
{
    public const string PromptVersionKey = "PromptVersion";
    public const string GoldenSetHashKey = "GoldenSetHash";
    public const string ModelIdKey = "ModelId";

    public static string FileName(ReportHeader header) =>
        $"{header.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{header.PromptVersion}-{UnsafeFileNameChars().Replace(header.ModelId, "-")}.md";

    public static string Render(ReportHeader header, AggregateMetrics aggregate, IReadOnlyList<CaseResult> cases)
    {
        var report = new StringBuilder();
        report.AppendLine("# Enrichment prompt evaluation").AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- {PromptVersionKey}: {header.PromptVersion}")
            .AppendLine(CultureInfo.InvariantCulture, $"- {GoldenSetHashKey}: {header.GoldenSetHash}")
            .AppendLine(CultureInfo.InvariantCulture, $"- {ModelIdKey}: {header.ModelId}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Provider: {header.Provider}")
            .AppendLine(CultureInfo.InvariantCulture, $"- RunsPerCase: {header.RunsPerCase}")
            .AppendLine(CultureInfo.InvariantCulture, $"- GeneratedAtUtc: {header.GeneratedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Status: {(aggregate.Incomplete ? "INCOMPLETE" : "COMPLETE")}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Result: {(aggregate.Passed ? "PASS" : "FAIL")}")
            .AppendLine()
            .AppendLine("Calls are sequential through one `ChatEnrichmentService`, so its circuit breaker is shared across calls:")
            .AppendLine("after a series of timeouts or provider errors, later calls end as `Unavailable` without reaching the model.")
            .AppendLine("Definitions and thresholds: `docs/adr/009-prompt-evaluation.md`.")
            .AppendLine();

        AppendAggregate(report, aggregate);
        AppendPerCase(report, cases);
        AppendRaw(report, header, cases);
        return report.ToString();
    }

    /// <summary>The <c>- Name: value</c> lines before the first section; what the CI guard reads.</summary>
    public static IReadOnlyDictionary<string, string> ReadHeader(string report)
    {
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in report.ReplaceLineEndings("\n").Split('\n').TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)))
        {
            if (HeaderLine().Match(line) is { Success: true } match)
            {
                header.TryAdd(match.Groups[1].Value, match.Groups[2].Value.Trim());
            }
        }

        return header;
    }

    private static void AppendAggregate(StringBuilder report, AggregateMetrics aggregate)
    {
        report.AppendLine("## Aggregate").AppendLine()
            .AppendLine("Mean of the per-case values; cases where a metric is n/a are left out.").AppendLine()
            .AppendLine("| Metric | Value | Threshold | Result |")
            .AppendLine("|---|---|---|---|");
        foreach (var threshold in aggregate.Thresholds)
        {
            var bound = (threshold.Kind == ThresholdKind.AtLeast ? "≥ " : "≤ ") + Number(threshold.Threshold);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {threshold.Metric} | {Number(threshold.Value)} | {bound} | {(threshold.Passed ? "pass" : "FAIL")} |");
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"| Mean confidence, seniority exact | {Number(aggregate.MeanConfidenceWhenExact)} | reported only | |")
            .AppendLine(CultureInfo.InvariantCulture, $"| Mean confidence, seniority not exact | {Number(aggregate.MeanConfidenceWhenNotExact)} | reported only | |")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture,
                $"Infrastructure failures: {aggregate.Timeouts} timeout(s), {aggregate.Unavailable} unavailable. Any of them makes the report INCOMPLETE.")
            .AppendLine();
    }

    private static void AppendPerCase(StringBuilder report, IReadOnlyList<CaseResult> cases)
    {
        report.AppendLine("## Per case").AppendLine()
            .AppendLine("| Case | Calls | Enriched | Invalid | Timeout | Unavailable | Valid rate | Recall | Precision | Hallucination | Seniority exact / also accepted / wrong | Seniority accepted | Stability | Confidence exact | Confidence not exact |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var m in cases.Select(c => c.Metrics))
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {m.CaseId} | {m.Calls} | {m.Enriched} | {m.InvalidOutput} | {m.Timeouts} | {m.Unavailable} | {Number(m.ValidRate)} | " +
                $"{Number(m.Recall)} | {Number(m.Precision)} | {Number(m.HallucinationRate)} | {m.SeniorityExact} / {m.SeniorityAlsoAccepted} / {m.SeniorityWrong} | " +
                $"{Number(m.SeniorityAccepted)} | {Number(m.Stability)} | {Number(m.MeanConfidenceWhenExact)} | {Number(m.MeanConfidenceWhenNotExact)} |");
        }

        report.AppendLine();
        var hallucinating = cases.Select(c => c.Metrics).Where(m => m.ForbiddenReturned.Count > 0).ToList();
        if (hallucinating.Count > 0)
        {
            report.AppendLine("Forbidden skills returned:").AppendLine();
            foreach (var m in hallucinating)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {m.CaseId}: {Skills(m.ForbiddenReturned)}");
            }

            report.AppendLine();
        }
    }

    private static void AppendRaw(StringBuilder report, ReportHeader header, IReadOnlyList<CaseResult> cases)
    {
        report.AppendLine("## Raw answers").AppendLine()
            .AppendLine(CultureInfo.InvariantCulture,
                $"Expected values are in `eval/cases/<case>.json` at GoldenSetHash {header.GoldenSetHash}.")
            .AppendLine();
        foreach (var result in cases)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"### {result.Case.Id}").AppendLine()
                .AppendLine("| Run | Outcome | Seniority | Confidence | Skills |")
                .AppendLine("|---|---|---|---|---|");
            for (var i = 0; i < result.Runs.Count; i++)
            {
                var row = result.Runs[i] switch
                {
                    EnrichmentOutcome.Enriched e =>
                        $"Enriched | {e.Enrichment.Seniority} | {e.Enrichment.Confidence.Value.ToString("0.####", CultureInfo.InvariantCulture)} | {Skills(e.Enrichment.Skills.Select(s => s.Name))}",
                    EnrichmentOutcome.Failed f => $"{f.Reason} | | |",
                    _ => throw new InvalidOperationException("Unknown enrichment outcome."),
                };
                report.AppendLine(CultureInfo.InvariantCulture, $"| {i + 1} | {row} |");
            }

            report.AppendLine();
        }
    }

    private static string Number(double? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a";

    // Each skill in its own code span: a skill name may contain a comma, so a plain list could not be split back.
    private static string Skills(IEnumerable<string> names) =>
        string.Join(" ", names.Select(n => $"`{n.Replace("|", "\\|", StringComparison.Ordinal)}`"));

    [GeneratedRegex(@"^- (\w+): (.+)$")]
    private static partial Regex HeaderLine();

    [GeneratedRegex("[^A-Za-z0-9.-]")]
    private static partial Regex UnsafeFileNameChars();
}
