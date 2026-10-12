using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TalentSync.Contracts;
using TalentSync.Infrastructure.Messaging.Handling;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;
using TalentSync.Infrastructure.Messaging.Persistence;

namespace TalentSync.Infrastructure.Messaging.Tests;

public class EventProcessingLoopTests
{
    private const int MaxUnclassifiedAttempts = 5;

    private readonly FakeStore _store = new();
    private readonly FakeHandler _handler = new();
    private int _checkpoints;

    [Fact]
    public async Task HandledEvent_WritesInboxAfterEffectThenCheckpoints()
    {
        var envelope = Envelope();

        await ProcessAsync(envelope);

        Assert.Equal(1, _handler.Calls);
        Assert.Equal(envelope.EventId, Assert.Single(_store.Inbox).EventId);
        Assert.Empty(_store.Parked);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task UnhandledType_IsCheckpointedWithoutInboxRow()
    {
        await ProcessAsync(Envelope(type: "job.create"));

        Assert.Equal(0, _handler.Calls);
        Assert.Empty(_store.Inbox);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task EventAlreadyInInbox_IsCheckpointedWithoutRunningTheHandler()
    {
        var envelope = Envelope();
        _store.Inbox.Add(new InboxEntry { Source = envelope.Source, EventId = envelope.EventId, ProcessedAt = DateTime.UtcNow });

        await ProcessAsync(envelope);

        Assert.Equal(0, _handler.Calls);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task UnreadableBody_IsParkedTruncatedAndCheckpointed()
    {
        var body = Encoding.UTF8.GetBytes("not json " + new string('x', 10_000));

        await ProcessAsync(body);

        var parked = Assert.Single(_store.Parked);
        Assert.Null(parked.EventId);
        Assert.Equal(EventProcessingLoop.UnreadableEnvelope, parked.Error);
        Assert.Equal(EventProcessingLoop.MaxParkedBodyBytes, parked.Envelope.Length);
        Assert.Equal(0, _handler.Calls);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task PermanentResult_IsParkedWithReasonAndCheckpointed()
    {
        var envelope = Envelope();
        _handler.Results.Enqueue(() => HandlerResult.Permanent("400 GET /v1/candidates"));

        await ProcessAsync(envelope);

        var parked = Assert.Single(_store.Parked);
        Assert.Equal(envelope.EventId, parked.EventId);
        Assert.Equal("400 GET /v1/candidates", parked.Error);
        Assert.Empty(_store.Inbox);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task TransientFailures_AreRetriedBeyondTheUnclassifiedLimitAndNeverParked()
    {
        for (var i = 0; i < MaxUnclassifiedAttempts * 2; i++)
        {
            _handler.Results.Enqueue(() => throw new HttpRequestException(null, null, HttpStatusCode.ServiceUnavailable));
        }

        await ProcessAsync(Envelope());

        Assert.Equal(MaxUnclassifiedAttempts * 2 + 1, _handler.Calls);
        Assert.Empty(_store.Parked);
        Assert.Single(_store.Inbox);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task UnclassifiedFailure_IsParkedAfterTheRetryLimit()
    {
        for (var i = 0; i < MaxUnclassifiedAttempts; i++)
        {
            _handler.Results.Enqueue(() => throw new InvalidOperationException("bug"));
        }

        await ProcessAsync(Envelope());

        Assert.Equal(MaxUnclassifiedAttempts, _handler.Calls);
        Assert.Equal("retry limit exceeded: System.InvalidOperationException", Assert.Single(_store.Parked).Error);
        Assert.Empty(_store.Inbox);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task UnclassifiedFailure_ThatRecoversBeforeTheLimit_IsHandled()
    {
        for (var i = 0; i < MaxUnclassifiedAttempts - 1; i++)
        {
            _handler.Results.Enqueue(() => throw new InvalidOperationException("bug"));
        }

        await ProcessAsync(Envelope());

        Assert.Empty(_store.Parked);
        Assert.Single(_store.Inbox);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task FailedParking_KeepsRetryingAndNeverCheckpointsUnparked()
    {
        for (var i = 0; i < MaxUnclassifiedAttempts + 2; i++)
        {
            _handler.Results.Enqueue(() => throw new InvalidOperationException("bug"));
        }

        _store.ParkFailures = 2;

        await ProcessAsync(Envelope());

        Assert.Single(_store.Parked);
        Assert.Equal(MaxUnclassifiedAttempts + 2, _handler.Calls);
        Assert.Equal(1, _checkpoints);
    }

    [Fact]
    public async Task Shutdown_ReturnsWithoutCheckpointOrParking()
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _handler.Results.Enqueue(() =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        });

        await Loop().ProcessAsync(Received(Serialize(Envelope())), CountCheckpoint, shutdown.Token);

        Assert.Equal(1, _handler.Calls);
        Assert.Empty(_store.Parked);
        Assert.Empty(_store.Inbox);
        Assert.Equal(0, _checkpoints);
    }

    [Fact]
    public async Task FailedCheckpoint_IsRetriedWithoutRepeatingTheEffect()
    {
        var failures = 1;
        var loop = Loop();

        await loop.ProcessAsync(Received(Serialize(Envelope())), _ =>
        {
            if (failures-- > 0)
            {
                throw new TimeoutException();
            }

            _checkpoints++;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, _handler.Calls);
        Assert.Single(_store.Inbox);
        Assert.Equal(1, _checkpoints);
    }

    private Task ProcessAsync(EventEnvelope envelope) => ProcessAsync(Serialize(envelope));

    private Task ProcessAsync(byte[] body) =>
        Loop().ProcessAsync(Received(body), CountCheckpoint, TestContext.Current.CancellationToken);

    private Task CountCheckpoint(CancellationToken cancellationToken)
    {
        _checkpoints++;
        return Task.CompletedTask;
    }

    private EventProcessingLoop Loop() => new(
        _handler,
        _store,
        Options.Create(new ConsumerOptions
        {
            InitialBackoff = TimeSpan.Zero,
            MaxBackoff = TimeSpan.Zero,
            MaxUnclassifiedAttempts = MaxUnclassifiedAttempts,
        }),
        TimeProvider.System,
        NullLogger<EventProcessingLoop>.Instance);

    private static ReceivedEvent Received(byte[] body) => new("0", 42, body);

    private static byte[] Serialize(EventEnvelope envelope) => Encoding.UTF8.GetBytes(EventEnvelopeJson.Serialize(envelope));

    private static EventEnvelope Envelope(string type = EventTypes.CandidateCreate) => new()
    {
        EventId = new string('e', 64),
        Source = "teamtailor",
        Type = type,
        SchemaVersion = EventEnvelope.CurrentSchemaVersion,
        ResourceType = "candidates",
        ResourceId = "c-1",
        CandidateId = "c-1",
        ReceivedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class FakeHandler : IEventHandler
    {
        /// <summary>One entry per call; an empty queue means <see cref="HandlerResult.Done"/>.</summary>
        public Queue<Func<HandlerResult>> Results { get; } = new();

        public int Calls { get; private set; }

        public bool Handles(string eventType) => EventTypes.IsCandidateEvent(eventType);

        public Task<HandlerResult> HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Results.TryDequeue(out var next) ? next() : HandlerResult.Done);
        }
    }

    private sealed class FakeStore : IConsumerStore
    {
        public List<InboxEntry> Inbox { get; } = [];

        public List<ParkedEvent> Parked { get; } = [];

        public int ParkFailures { get; set; }

        public Task<bool> IsHandledAsync(string source, string eventId, CancellationToken cancellationToken) =>
            Task.FromResult(Inbox.Any(e => e.Source == source && e.EventId == eventId));

        public Task MarkHandledAsync(InboxEntry entry, CancellationToken cancellationToken)
        {
            Inbox.Add(entry);
            return Task.CompletedTask;
        }

        public Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken)
        {
            if (ParkFailures-- > 0)
            {
                throw new InvalidOperationException("parking lot unavailable");
            }

            Parked.Add(parkedEvent);
            return Task.CompletedTask;
        }
    }
}
