using System.Collections.Concurrent;
using System.Diagnostics;
using TalentSync.Infrastructure.Messaging.Handling;

namespace TalentSync.Infrastructure.Messaging.Tests;

/// <summary>Records the consumer skeleton's spans in the order they end.</summary>
internal sealed class SpanCollector : IDisposable
{
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public SpanCollector()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ConsumerTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Stopped => [.. _stopped];

    public void Dispose() => _listener.Dispose();
}
