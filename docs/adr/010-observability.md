# ADR 010: Observability

## Status

Accepted

## Context

One webhook crosses four processes and two asynchronous hops: Ingress stores it in the outbox, the relay publishes
it later, and two consumer groups process it independently. Every host logs good ids, but following one webhook means
searching three hosts' logs by `EventId`. Tracing breaks at the outbox (the request has ended before the publish),
at Event Hubs (the envelope carries ids only) and in the retry loop (one call can last for an entire outage).

## Decision

- **OpenTelemetry over OTLP**, W3C trace context. Locally the Aspire Dashboard runs in docker compose. The exporter
  is registered only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so tests and CI export nothing. Simulators are not
  instrumented: they stand in for external systems.
- **One project, `TalentSync.Infrastructure.Observability`** (`AddObservability`), referenced only by the hosts.
  It holds one concern, so it is not a Common project (ADR 001). Our spans and metrics use the BCL `ActivitySource`
  and `Meter` where the work happens; the SDK subscribes to `TalentSync.*`. No telemetry in `*.Domain` or `*.Application`.
- **Explicit propagation, one trace per webhook.** Our hosts are the edge and ignore the trace context of incoming
  requests: the sender's context is untrusted, and one sender trace can carry several webhooks. So the webhook
  request span is the root of a new trace, and the webhook span is its child.
  Ingress stores the webhook span's context in `ingress.Outbox.TraceParent`.
  The relay's publish span is a child of it and writes `traceparent` to `EventData.Properties`, never to the envelope.
  The consumer span is its child. A missing or invalid value starts a new trace and never fails the event.
- **One consumer span per attempt**, tagged with the outcome (`handled`, `skipped_duplicate`, `parked`, ...).
  A span is exported when it ends, so one span around the retry loop would stay invisible during an outage.
- **Metrics** answer what a trace cannot: consumer attempts by outcome, event age from webhook to effect, outbox
  publish delay, LLM calls by outcome and tokens billed. The LLM call is a span; a reused stored suggestion has none.
- **Logs carry the trace** (OpenTelemetry logging provider) and a scope with `EventId`, `EventType`, `CandidateId`.
- **Invariant 14 covers telemetry:** ids only; never the CV, the prompt, model output or response bodies.

## Consequences

- One trace shows the request, the outbox delay, both consumer groups and every HTTP and LLM call.
- A relay double publish shows up in the same trace as a `skipped_duplicate` branch. A webhook that Teamtailor sends
  twice is a new trace, found by the `EventId` tag.
- One more column and migration in `ingress`. Rows published before it start their own trace.
- Consumer spans can start long after their parent ended (outage, replay). Valid, but long traces are harder to read.
- OpenTelemetry packages in one project, one more container in compose.

## Alternatives considered

- **The Event Hubs SDK's own propagation.** Rejected: experimental switch, hidden in a library.
- **Trace context in `EventEnvelope`.** Rejected: the envelope is a business contract, not transport metadata.
- **A span link instead of a parent at the outbox.** Rejected: the dashboard would split one webhook into separate traces.
- **Continuing the sender's trace.** Rejected: an external caller would choose our trace id and its sampling.
- **A new trace at the webhook span, linked to the request span.** Rejected: dashboards show links only in span
  details, so the request and the flow it starts would be separate traces.
- **Setup duplicated in each host.** Rejected: three copies would drift.
- **Seq or Grafana.** Seq covers logs only; Grafana is heavier. Production only needs another OTLP endpoint.
