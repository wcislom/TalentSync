using System.Diagnostics;
using OpenTelemetry;

namespace TalentSync.Infrastructure.Observability;

/// <summary>
/// Decides what of a span leaves the process (ADR 010), for our spans and the libraries' alike. Registered before the
/// exporter, which skips a span that is not recorded.
/// </summary>
internal sealed class SpanExportPolicy : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        // A client span without a parent belongs to no flow: the relay polling the outbox, migrations. Each poll would
        // be a trace of its own and bury the flow. Calls inside a flow (inbox, parking lot) have a parent and are kept.
        if (activity.Kind == ActivityKind.Client && activity.ParentSpanId == default)
        {
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
            return;
        }

        // Invariant 14: a status description is an exception message, which can echo a request (a provider's error,
        // a duplicate key). The status code and error.type stay; the message is in the log, which carries the trace id.
        if (activity.StatusDescription is not null)
        {
            activity.SetStatus(activity.Status);
        }
    }
}
