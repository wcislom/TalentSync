using System.Text.Json.Nodes;
using FakeTeamtailor.State;

namespace FakeTeamtailor.Teamtailor;

/// <summary>
/// The only place that knows Teamtailor's JSON shapes: JSON:API resources returned by <c>/v1</c> and the
/// <c>data</c> object of webhooks. Keys are written out, kebab-case, as in the Teamtailor docs.
/// </summary>
public static class TeamtailorJson
{
    // ---- /v1 resources ----

    public static JsonObject CandidateResource(string id, Candidate candidate) => new()
    {
        ["id"] = id,
        ["type"] = "candidates",
        ["attributes"] = new JsonObject
        {
            ["first-name"] = candidate.FirstName,
            ["last-name"] = candidate.LastName,
            ["email"] = candidate.Email,
            ["phone"] = candidate.Phone,
            // ASSUMPTION (teamtailor rule): the real API has no plain-text CV attribute.
            ["resume-text"] = candidate.ResumeText,
            ["created-at"] = candidate.CreatedAt,
            ["updated-at"] = candidate.UpdatedAt,
        },
    };

    public static JsonObject JobApplicationResource(string id, JobApplication application) => new()
    {
        ["id"] = id,
        ["type"] = "job-applications",
        ["attributes"] = new JsonObject
        {
            ["created-at"] = application.CreatedAt,
            ["updated-at"] = application.UpdatedAt,
            ["rejected-at"] = application.RejectedAt,
        },
        ["relationships"] = new JsonObject
        {
            ["candidate"] = Relationship(application.CandidateId, "candidates"),
            ["job"] = Relationship(application.JobId, "jobs"),
            ["stage"] = Relationship(application.StageId, "stages"),
        },
    };

    public static JsonObject StageResource(string id, Stage stage) => new()
    {
        ["id"] = id,
        ["type"] = "stages",
        ["attributes"] = new JsonObject { ["name"] = stage.Name },
    };

    // ---- webhook data ----

    // No CV: webhook payloads are partial, the workers fetch the candidate from /v1.
    public static JsonObject CandidateWebhookData(string id, Candidate candidate) => new()
    {
        ["id"] = id,
        ["type"] = "candidates",
        ["attributes"] = new JsonObject
        {
            ["first-name"] = candidate.FirstName,
            ["last-name"] = candidate.LastName,
            ["email"] = candidate.Email,
            ["phone"] = candidate.Phone,
            ["created-at"] = candidate.CreatedAt,
            ["updated-at"] = candidate.UpdatedAt,
        },
    };

    // destroy events carry a reduced data object.
    public static JsonObject CandidateDestroyWebhookData(string id) => new()
    {
        ["id"] = id,
        ["type"] = "candidates",
    };

    public static JsonObject JobApplicationWebhookData(string id, JobApplication application, string stageName) => new()
    {
        ["id"] = id,
        ["type"] = "job-applications",
        ["attributes"] = new JsonObject
        {
            ["job-id"] = application.JobId,
            ["stage-name"] = stageName,
            ["rejected-at"] = application.RejectedAt,
            ["updated-at"] = application.UpdatedAt,
        },
        // ASSUMPTION (teamtailor rule): not listed in the docs, but Ingress needs it as the partition key.
        ["relationships"] = new JsonObject
        {
            ["candidate"] = Relationship(application.CandidateId, "candidates"),
        },
    };

    private static JsonObject Relationship(string id, string type) => new()
    {
        ["data"] = new JsonObject { ["id"] = id, ["type"] = type },
    };
}
