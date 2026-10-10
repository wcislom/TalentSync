using System.Text;
using TalentSync.Infrastructure.Teamtailor.Webhooks;

namespace TalentSync.Infrastructure.Teamtailor.Tests;

public class TeamtailorWebhookParserTests
{
    private readonly TeamtailorWebhookParser _parser = new();

    [Fact]
    public void CandidateEvent_UsesDataIdAsCandidateId()
    {
        var result = Parse("""
            {
              "payload": {
                "event_name": "candidate.create",
                "data": { "id": "c-1", "type": "candidates", "attributes": { "first-name": "Jane" } }
              },
              "signature": "ignored"
            }
            """);

        var parsed = Assert.IsType<WebhookParseResult.Parsed>(result);
        Assert.Equal(new WebhookNotification("candidate.create", "candidates", "c-1", "c-1"), parsed.Notification);
    }

    [Fact]
    public void JobApplicationEvent_TakesCandidateIdFromRelationships()
    {
        var result = Parse("""
            {
              "payload": {
                "event_name": "job_application.update",
                "data": {
                  "id": "ja-7",
                  "type": "job-applications",
                  "attributes": { "stage-name": "Qualified" },
                  "relationships": { "candidate": { "data": { "id": "c-2", "type": "candidates" } } }
                }
              }
            }
            """);

        var parsed = Assert.IsType<WebhookParseResult.Parsed>(result);
        Assert.Equal(
            new WebhookNotification("job_application.update", "job-applications", "ja-7", "c-2"),
            parsed.Notification);
    }

    [Fact]
    public void JobApplicationEvent_WithoutCandidateRelationship_IsInvalid()
    {
        var result = Parse("""
            { "payload": { "event_name": "job_application.destroy", "data": { "id": "ja-7", "type": "job-applications" } } }
            """);

        var invalid = Assert.IsType<WebhookParseResult.Invalid>(result);
        Assert.Contains("relationships.candidate", invalid.Reason);
    }

    [Fact]
    public void NumericIds_AreAccepted()
    {
        var result = Parse("""
            { "payload": { "event_name": "candidate.update", "data": { "id": 42, "type": "candidates" } } }
            """);

        var parsed = Assert.IsType<WebhookParseResult.Parsed>(result);
        Assert.Equal("42", parsed.Notification.CandidateId);
    }

    [Theory]
    [InlineData("job.update")]
    [InlineData("something.new")]
    public void EventWithoutCandidate_IsIgnored(string eventName)
    {
        var result = Parse($$"""
            { "payload": { "event_name": "{{eventName}}", "data": { "id": "j-1", "type": "jobs" } } }
            """);

        var ignored = Assert.IsType<WebhookParseResult.Ignored>(result);
        Assert.Equal(eventName, ignored.EventName);
    }

    [Theory]
    [InlineData("not json", "malformed JSON")]
    [InlineData("[]", "missing payload")]
    [InlineData("""{ "payload": { "data": { "id": "c-1", "type": "candidates" } } }""", "missing payload.event_name")]
    [InlineData("""{ "payload": { "event_name": "candidate.create" } }""", "missing payload.data")]
    [InlineData("""{ "payload": { "event_name": "candidate.create", "data": { "type": "candidates" } } }""", "missing payload.data.id")]
    [InlineData("""{ "payload": { "event_name": "candidate.create", "data": { "id": "", "type": "candidates" } } }""", "missing payload.data.id")]
    [InlineData("""{ "payload": { "event_name": "candidate.create", "data": { "id": "c-1" } } }""", "missing payload.data.type")]
    public void UnreadableBody_IsInvalid_WithReasonNamingTheField(string body, string expectedReason)
    {
        var invalid = Assert.IsType<WebhookParseResult.Invalid>(Parse(body));
        Assert.Equal(expectedReason, invalid.Reason);
    }

    [Fact]
    public void InvalidReason_NeverContainsAttributeValues()
    {
        var invalid = Assert.IsType<WebhookParseResult.Invalid>(Parse("""
            { "payload": { "event_name": "candidate.create", "data": { "type": "candidates", "attributes": { "email": "jane@example.com" } } } }
            """));

        Assert.DoesNotContain("example.com", invalid.Reason);
    }

    private WebhookParseResult Parse(string body) => _parser.Parse(Encoding.UTF8.GetBytes(body));
}
