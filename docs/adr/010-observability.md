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
  is registered only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so tests and CI export nothing; `OTEL_SERVICE_NAME`
  is then required. Simulators are not instrumented: they stand in for external systems.
- **One project, `TalentSync.Infrastructure.Observability`** (`AddObservability`), referenced only by the hosts.
  It holds one concern, so it is not a Common project (ADR 001). The SDK subscribes to `TalentSync.*`.
  No telemetry in `*.Domain` or `*.Application`.
- **One telemetry class per context, where the work happens:** `IngressTelemetry`, `ConsumerTelemetry`
  (`Infrastructure.Messaging`), `MatchingTelemetry`. Each holds a static BCL `ActivitySource` and its instruments,
  created through `IMeterFactory`, is a singleton injected into the code it observes, and never throws: an unknown
  outcome is tagged `unknown`, never an exception that would fail the event.
- **The LLM call is instrumented by `OpenTelemetryChatClient`** (`Microsoft.Extensions.AI`, gen_ai semantic
  conventions: the `chat` span, token usage and duration) under the source name `TalentSync.Matching`.
- **SQL calls are spans** through the SqlClient instrumentation, which also sees EF Core's commands.
- **Names follow the semantic conventions** where they exist: `send ats-events` and `process ats-events` with
  `messaging.*` attributes, `error.type`, `gen_ai.*`. Our own attributes are `talentsync.<concern>.<name>`
  (`talentsync.consumer.outcome`, `talentsync.enrichment.outcome`); ids are `talentsync.event_id`,
  `talentsync.event_type`, `talentsync.candidate_id` in spans and logs alike.
- **Explicit propagation, one trace per webhook.** Our hosts are the edge and ignore the trace context of incoming
  requests: the sender's context is untrusted, and one sender trace can carry several webhooks. So the webhook
  request span is the root of a new trace, and the webhook span is its child.
  Ingress stores the webhook span's context in `ingress.Outbox.TraceParent`.
  The relay's send span is a child of it and writes `traceparent` to `EventData.Properties`, never to the envelope.
  The consumer span is its child. A missing or invalid value starts a new trace and never fails the event.
- **One consumer span per attempt**, tagged with the outcome (`handled`, `skipped_duplicate`, `parked`, ...).
  A span is exported when it ends, so one span around the retry loop would stay invisible during an outage.
- **Metrics** answer what a trace cannot: consumer attempts by outcome, event age from webhook to effect, outbox
  publish delay, enrichment calls by outcome, and the gen_ai token usage and call duration. The LLM call is a span;
  a reused stored suggestion has none. Our histograms are in seconds and set their bucket boundaries as instrument
  advice: the SDK's default buckets are sized for milliseconds.
- **Logs carry the trace** (OpenTelemetry logging provider) and a scope with the event's ids, named like the span tags.
- **Invariant 14 covers telemetry:** ids only; never the CV, the prompt, model output or response bodies. A failed span
  carries the error status and `error.type`, never the exception message, which can echo a request: the message and
  stack trace are in the log, which carries the trace id. `SpanExportPolicy` clears the status description of every
  span before export, ours and the libraries' alike. `OpenTelemetryChatClient.EnableSensitiveData` is set to false
  explicitly, so `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` cannot turn it on.
- **A client span without a parent is not exported** (`SpanExportPolicy`): the relay polling the outbox, the event
  processor claiming partitions in Blob storage, migrations. It belongs to no flow, and every poll would be a trace.

## Consequences

- One trace shows the request, the outbox delay, both consumer groups and every HTTP, SQL and LLM call, the Event
  Hubs checkpoint included.
- A relay double publish shows up in the same trace as a `skipped_duplicate` branch. A webhook that Teamtailor sends
  twice is a new trace, found by the `EventId` tag.
- One more column and migration in `ingress`. Rows published before it start their own trace.
- Consumer spans can start long after their parent ended (outage, replay). Valid, but long traces are harder to read.
- OpenTelemetry packages in one project, one more container in compose, `Microsoft.Extensions.AI` in
  `Matching.Infrastructure`. The gen_ai conventions are still experimental, so their names can change with the package.
- Our hosts ignore the sender's trace context only because the ASP.NET Core instrumentation does not extract it again
  when the propagator is exactly `TraceContextPropagator`. That is the instrumentation's internal behaviour, pinned by
  `EdgeTraceTests` with the SDK enabled; scenario O1 runs without it.
- HTTP client metrics include the Blob SDK's polling: unlike spans, they cannot be filtered per request.

## Alternatives considered

- **The Event Hubs SDK's own propagation.** Rejected: experimental switch, hidden in a library.
- **Trace context in `EventEnvelope`.** Rejected: the envelope is a business contract, not transport metadata.
- **A span link instead of a parent at the outbox.** Rejected: the dashboard would split one webhook into separate traces.
- **Continuing the sender's trace.** Rejected: an external caller would choose our trace id and its sampling.
- **A new trace at the webhook span, linked to the request span.** Rejected: dashboards show links only in span
  details, so the request and the flow it starts would be separate traces.
- **Setup duplicated in each host.** Rejected: three copies would drift.
- **A hand-written gen_ai decorator.** Rejected: `OpenTelemetryChatClient` implements the conventions, metrics
  included, for one package.
- **EF Core instrumentation.** Rejected: still a beta package; the SqlClient instrumentation is stable and sees EF's
  commands.
- **Static `Meter`s.** Rejected: `IMeterFactory` ties the meters to the host's container, and tests create their own.
- **The exception on the span (`AddException`).** Rejected: its message can carry data from the request (invariant 14),
  and the log already holds it under the same trace id.
- **Seq or Grafana.** Seq covers logs only; Grafana is heavier. Production only needs another OTLP endpoint.
