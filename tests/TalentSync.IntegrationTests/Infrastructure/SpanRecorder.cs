using System.Collections.Concurrent;
using System.Diagnostics;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>
/// Records our spans (ADR 010). Every host runs in this process, so one listener sees the spans of all of them,
/// without an exporter. The listener is process-wide: while it lives, the other hosts' events create spans too.
/// </summary>
public sealed class SpanRecorder : IDisposable
{
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public SpanRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("TalentSync.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Ended spans tagged with this event id.</summary>
    public IReadOnlyList<Activity> ForEvent(string eventId) =>
        _stopped.Where(span => span.GetTagItem("talentsync.event_id") as string == eventId).ToList();

    public void Dispose() => _listener.Dispose();
}
