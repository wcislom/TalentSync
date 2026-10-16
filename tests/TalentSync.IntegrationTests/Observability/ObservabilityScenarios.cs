using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TalentSync.Contracts;
using TalentSync.IntegrationTests.Infrastructure;

namespace TalentSync.IntegrationTests.Observability;

/// <summary>Scenario O1 of <c>docs/acceptance-scenarios.md</c>.</summary>
[Collection(SystemCollection.Name)]
public sealed class ObservabilityScenarios(SystemFixture fixture)
{
    private const string SyncWorker = "sync-worker";
    private const string MatchingWorker = "matching-worker";

    [Fact]
    public async Task O1_OneWebhookProducesOneTraceThroughIngressTheRelayAndBothWorkers()
    {
        const string candidateId = "c-o1";
        using var recorder = new SpanRecorder();

        await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Olga", "Wrona", "olga.wrona@example.com");
        var eventId = await fixture.QueryIngressDbAsync(db => db.Outbox
            .Where(m => m.CandidateId == candidateId && m.Type == EventTypes.CandidateCreate)
            .Select(m => m.EventId)
            .SingleAsync());

        // Not part of the Then: spans are recorded when they end, so wait until both consumer groups are done.
        var spans = await Eventually.GetAsync(
            () =>
            {
                var recorded = recorder.ForEvent(eventId);
                return Task.FromResult(HasProcessSpan(recorded, SyncWorker) && HasProcessSpan(recorded, MatchingWorker) ? recorded : null);
            },
            $"process spans of {eventId} in both consumer groups");

        var webhook = Assert.Single(spans, span => span.Kind == ActivityKind.Internal);
        var request = recorder.Find(webhook.ParentSpanId);
        Assert.NotNull(request);
        Assert.Equal(ActivityKind.Server, request.Kind);
        Assert.Equal(webhook.TraceId, request.TraceId);
        Assert.Equal(default, request.ParentSpanId);

        var publish = Assert.Single(spans, span => span.Kind == ActivityKind.Producer);
        Assert.Equal(webhook.TraceId, publish.TraceId);
        Assert.Equal(webhook.SpanId, publish.ParentSpanId);

        foreach (var consumerGroup in new[] { SyncWorker, MatchingWorker })
        {
            var processSpans = ProcessSpans(spans, consumerGroup);
            Assert.NotEmpty(processSpans);
            Assert.All(processSpans, process =>
            {
                Assert.Equal(webhook.TraceId, process.TraceId);
                Assert.Equal(publish.SpanId, process.ParentSpanId);
            });
        }
    }

    private static bool HasProcessSpan(IReadOnlyList<Activity> spans, string consumerGroup) => ProcessSpans(spans, consumerGroup).Count > 0;

    private static List<Activity> ProcessSpans(IReadOnlyList<Activity> spans, string consumerGroup) =>
        spans.Where(span => span.Kind == ActivityKind.Consumer
            && span.GetTagItem("messaging.consumer.group.name") as string == consumerGroup).ToList();
}
