extern alias FakeTeamtailor;

using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using FakeTeamtailor::FakeTeamtailor.Teamtailor;
using TalentSync.IntegrationTests.Infrastructure;

namespace TalentSync.IntegrationTests.Ingress;

/// <summary>Scenarios I1–I3 of <c>docs/acceptance-scenarios.md</c>.</summary>
[Collection(SystemCollection.Name)]
public sealed class IngressScenarios(SystemFixture fixture)
{
    [Fact]
    public async Task I1_AcceptedWebhookIsPublishedToTheHub()
    {
        const string candidateId = "c-i1";

        var response = await SendWebhookAsync($$"""
            { "payload": { "event_name": "job_application.update", "data": {
                "id": "ja-i1", "type": "job-applications",
                "relationships": { "candidate": { "data": { "id": "{{candidateId}}", "type": "candidates" } } } } } }
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var eventId = await GetEventIdAsync(candidateId);

        var events = await Eventually.GetAsync(() => ReadFromHubAsync(eventId), $"event {eventId} in the hub");
        var published = Assert.Single(events);
        Assert.Equal(candidateId, published.PartitionKey);

        await Eventually.GetAsync(
            () => fixture.QueryIngressDbAsync(db => db.Outbox.SingleOrDefaultAsync(m => m.EventId == eventId && m.PublishedAt != null)),
            $"PublishedAt of outbox row {eventId}");
    }

    [Fact]
    public async Task I2_EventHubsOutageDelaysButDoesNotLoseEvents()
    {
        const string candidateId = "c-i2";

        await fixture.EventHubs.PauseAsync(TestContext.Current.CancellationToken);
        string eventId;
        try
        {
            var response = await SendWebhookAsync($$"""
                { "payload": { "event_name": "candidate.update", "data": { "id": "{{candidateId}}", "type": "candidates" } } }
                """);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            eventId = await GetEventIdAsync(candidateId);

            // Not part of the Then: without these waits Event Hubs could be back before a send ever failed, and the
            // test would pass even if a failed send lost the event. First the relay holds the row during a send,
            // then the send fails and the row is released, still unpublished.
            await Eventually.GetAsync(
                async () => await fixture.CountClaimableOutboxRowsAsync(candidateId) == 0 ? "claimed" : null,
                $"the relay sending {eventId} while Event Hubs is unavailable");
            await Eventually.GetAsync(
                async () => await fixture.CountClaimableOutboxRowsAsync(candidateId) == 1 ? "released" : null,
                $"the failed send of {eventId} leaving its row unpublished");
        }
        finally
        {
            await fixture.EventHubs.UnpauseAsync(TestContext.Current.CancellationToken);
        }

        await Eventually.GetAsync(() => ReadFromHubAsync(eventId), $"event {eventId} in the hub");
    }

    [Fact]
    public async Task I3_WebhookWithAnInvalidSignatureIsRejected()
    {
        const string candidateId = "c-i3";

        var response = await SendWebhookAsync($$"""
            { "payload": { "event_name": "candidate.update", "data": { "id": "{{candidateId}}", "type": "candidates" } } }
            """,
            signatureKey: "another-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Ingress stores before it answers, so no wait is needed for the absence.
        Assert.False(await fixture.QueryIngressDbAsync(db => db.Outbox.AnyAsync(m => m.CandidateId == candidateId)));
    }

    private Task<HttpResponseMessage> SendWebhookAsync(string body, string signatureKey = SystemFixture.WebhookSignatureKey)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        content.Headers.Add("TT-Signature", WebhookSignature.Sign(signatureKey, Encoding.UTF8.GetBytes(body)));
        return fixture.Ingress.PostAsync("/webhooks/teamtailor", content, TestContext.Current.CancellationToken);
    }

    /// <summary>The webhook is stored before Ingress answers, so its row exists once the response is in.</summary>
    private Task<string> GetEventIdAsync(string candidateId) =>
        fixture.QueryIngressDbAsync(db => db.Outbox
            .Where(m => m.CandidateId == candidateId)
            .Select(m => m.EventId)
            .SingleAsync());

    private async Task<IReadOnlyList<HubEvent>?> ReadFromHubAsync(string eventId)
    {
        var events = await fixture.Hub.ReadByEventIdAsync(eventId);
        return events.Count > 0 ? events : null;
    }
}
