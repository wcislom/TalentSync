using Microsoft.EntityFrameworkCore;
using TalentSync.Contracts;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.IntegrationTests.Infrastructure;

namespace TalentSync.IntegrationTests.Sync;

/// <summary>Scenarios S1–S8 of <c>docs/acceptance-scenarios.md</c> (S7 is manual).</summary>
[Collection(SystemCollection.Name)]
public sealed class SyncScenarios(SystemFixture fixture)
{
    [Fact]
    public async Task S1_NewCandidateCreatesCrmContact()
    {
        const string candidateId = "c-s1";

        await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Alicja", "Nowak", "alicja.nowak@example.com");

        var contact = await Eventually.GetAsync(() => fixture.FakeCrm.FindContactAsync(candidateId), $"CRM contact {candidateId}");
        Assert.Equal("Alicja", contact.FirstName);
        Assert.Equal("Nowak", contact.LastName);
        Assert.Equal("alicja.nowak@example.com", contact.Email);
    }

    [Fact]
    public async Task S2_DuplicateWebhookProducesOneCrmCall()
    {
        const string candidateId = "c-s2";
        const int deliveries = 3;
        await fixture.FakeTeamtailor.SetAnomalyAsync(FakeTeamtailorTestClient.DuplicateWebhook, candidateId, count: deliveries);

        await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Dawid", "Kaminski", "dawid.kaminski@example.com");
        var eventId = await GetEventIdAsync(candidateId, EventTypes.CandidateCreate);

        // Not part of the Then: the inbox row appears with the first copy, while the other two may still wait in the
        // partition. Counting then would pass even without deduplication, so wait until the worker has checkpointed
        // the last copy: one partition key, one partition, processed in order.
        var published = await Eventually.GetAsync(
            async () => await fixture.Hub.ReadByEventIdAsync(eventId) is { Count: deliveries } events ? events : null,
            $"{deliveries} copies of {eventId} in the hub");
        var last = published.MaxBy(e => e.SequenceNumber)!;
        await Eventually.GetAsync(
            async () => await fixture.SyncCheckpoints.GetSequenceNumberAsync(last.PartitionId) >= last.SequenceNumber ? "checkpointed" : null,
            $"sync-worker checkpoint past the last copy of {eventId}");

        var calls = await fixture.FakeCrm.GetCallsAsync(candidateId);
        Assert.Equal(1, calls.CountOf("PUT", $"/contacts/{candidateId}"));
        Assert.Equal(1, await fixture.QuerySyncDbAsync(db => db.Inbox.CountAsync(e => e.EventId == eventId)));
    }

    [Fact]
    public async Task S3_DeletedCandidateMarksTheCrmContactAsDeleted()
    {
        const string candidateId = "c-s3";
        await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Bartosz", "Zielinski", "bartosz.zielinski@example.com");
        await Eventually.GetAsync(() => fixture.FakeCrm.FindContactAsync(candidateId), $"CRM contact {candidateId}");

        await fixture.FakeTeamtailor.DeleteCandidateAsync(candidateId);
        var eventId = await GetEventIdAsync(candidateId, EventTypes.CandidateDestroy);

        await Eventually.GetAsync(
            async () => await fixture.FakeCrm.FindContactAsync(candidateId) is { Status: "Deleted" } contact ? contact : null,
            $"CRM contact {candidateId} marked as deleted");
        // Not part of the Then: the inbox row is written after the effect, so the event is fully handled and the
        // counts below are final.
        await WaitUntilHandledAsync(eventId);

        var calls = await fixture.FakeCrm.GetCallsAsync(candidateId);
        Assert.Equal(1, calls.CountOf("DELETE", $"/contacts/{candidateId}"));
        Assert.Equal(0, await CountParkedAsync(eventId));
    }

    [Fact]
    public async Task S8_DeletingACandidateWithoutACrmContactSucceeds()
    {
        const string candidateId = "c-s8";
        await fixture.FakeTeamtailor.SetAnomalyAsync(FakeTeamtailorTestClient.DropWebhook, candidateId);
        await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Celina", "Wojcik", "celina.wojcik@example.com");
        await fixture.FakeTeamtailor.ClearAnomaliesAsync(candidateId);
        Assert.Null(await fixture.FakeCrm.FindContactAsync(candidateId));

        await fixture.FakeTeamtailor.DeleteCandidateAsync(candidateId);
        var eventId = await GetEventIdAsync(candidateId, EventTypes.CandidateDestroy);

        await WaitUntilHandledAsync(eventId);

        var calls = await fixture.FakeCrm.GetCallsAsync(candidateId);
        Assert.Equal(1, calls.CountOf("DELETE", $"/contacts/{candidateId}"));
        Assert.Equal(0, await CountParkedAsync(eventId));
        Assert.Equal(1, await fixture.QuerySyncDbAsync(db => db.Inbox.CountAsync(e => e.EventId == eventId)));
    }

    /// <summary>Ingress stores the webhook before it answers, and FakeTeamtailor waits for that answer.</summary>
    private Task<string> GetEventIdAsync(string candidateId, string eventType) =>
        fixture.QueryIngressDbAsync(db => db.Outbox
            .Where(m => m.CandidateId == candidateId && m.Type == eventType)
            .Select(m => m.EventId)
            .Distinct()
            .SingleAsync());

    private Task<InboxEntry> WaitUntilHandledAsync(string eventId) =>
        Eventually.GetAsync(
            () => fixture.QuerySyncDbAsync(db => db.Inbox.SingleOrDefaultAsync(e => e.EventId == eventId)),
            $"sync.Inbox row for {eventId}");

    private Task<int> CountParkedAsync(string eventId) =>
        fixture.QuerySyncDbAsync(db => db.ParkingLot.CountAsync(e => e.EventId == eventId));
}
