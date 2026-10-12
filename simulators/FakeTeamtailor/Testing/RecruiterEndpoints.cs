using FakeTeamtailor.State;
using FakeTeamtailor.Teamtailor;

namespace FakeTeamtailor.Testing;

// Teamtailor collects applications through a form with a CV file upload; that is irrelevant to the simulation,
// because TalentSync only reads the CV back from the API as text. JSON keeps tests and .http files short.
public sealed record ApplyRequest(string? CandidateId, string? FirstName, string? LastName, string? Email, string? Phone, string? ResumeText);

public sealed record StageChange(string Stage);

public sealed record CandidateChange(string? FirstName, string? LastName, string? Email, string? Phone, string? ResumeText);

/// <summary>
/// Scenario driving for tests and <c>.http</c> files. Not part of the Teamtailor API: each state change here
/// sends the webhook that Teamtailor would send for it. Responses list the deliveries, so a test can see
/// whether Ingress accepted them.
/// </summary>
public static class RecruiterEndpoints
{
    public static void MapRecruiter(this IEndpointRouteBuilder app)
    {
        var recruiter = app.MapGroup("/recruiter");

        recruiter.MapGet("/jobs", (Store store) =>
            store.Jobs.OrderBy(job => job.Key).Select(job => new { id = job.Key, job.Value.Title }));

        recruiter.MapPost("/jobs/{jobId}/apply", ApplyAsync);
        recruiter.MapPatch("/job-applications/{id}/stage", ChangeStageAsync);
        recruiter.MapDelete("/job-applications/{id}", DeleteJobApplicationAsync);
        recruiter.MapPatch("/candidates/{id}", UpdateCandidateAsync);
        recruiter.MapDelete("/candidates/{id}", DeleteCandidateAsync);
    }

    private static async Task<IResult> ApplyAsync(
        string jobId,
        ApplyRequest request,
        Store store,
        WebhookSender webhooks,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!store.Jobs.ContainsKey(jobId))
        {
            return Results.NotFound($"Unknown job {jobId}");
        }

        var now = timeProvider.GetUtcNow();
        var deliveries = new List<WebhookDelivery>();
        var candidateId = string.IsNullOrWhiteSpace(request.CandidateId) ? store.NewCandidateId() : request.CandidateId;

        if (!store.Candidates.ContainsKey(candidateId))
        {
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName)
                || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.ResumeText))
            {
                return Results.BadRequest("A new candidate needs firstName, lastName, email and resumeText");
            }

            var candidate = new Candidate(
                request.FirstName, request.LastName, request.Email, request.Phone, request.ResumeText, now, now);

            // Lost race with a parallel apply for the same id: that call created the candidate and sent the webhook.
            if (store.Candidates.TryAdd(candidateId, candidate))
            {
                deliveries.AddRange(await webhooks.SendAsync(
                    "candidate.create", TeamtailorJson.CandidateWebhookData(candidateId, candidate), cancellationToken));
            }
        }

        var newStageId = store.FindStageId("New") ?? throw new InvalidOperationException("Stage New is not seeded.");
        var applicationId = store.NewJobApplicationId();
        var application = new JobApplication(candidateId, jobId, newStageId, now, now, RejectedAt: null);
        store.JobApplications[applicationId] = application;
        deliveries.AddRange(await webhooks.SendAsync(
            "job_application.create",
            TeamtailorJson.JobApplicationWebhookData(applicationId, application, store.Stages[newStageId].Name),
            cancellationToken));

        return Results.Created(
            $"/v1/job-applications/{applicationId}",
            new { candidateId, jobApplicationId = applicationId, webhooks = deliveries });
    }

    private static async Task<IResult> ChangeStageAsync(
        string id,
        StageChange change,
        Store store,
        WebhookSender webhooks,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!store.JobApplications.TryGetValue(id, out var application))
        {
            return Results.NotFound($"Unknown job application {id}");
        }

        var stageId = store.FindStageId(change.Stage);
        if (stageId is null)
        {
            return Results.BadRequest($"Unknown stage {change.Stage}; known: {string.Join(", ", store.Stages.Values.Select(s => s.Name))}");
        }

        if (stageId == application.StageId)
        {
            return Results.Ok(new { jobApplicationId = id, webhooks = Array.Empty<WebhookDelivery>() });
        }

        var updated = application with { StageId = stageId, UpdatedAt = timeProvider.GetUtcNow() };
        store.JobApplications[id] = updated;
        var sent = await webhooks.SendAsync(
            "job_application.update",
            TeamtailorJson.JobApplicationWebhookData(id, updated, store.Stages[stageId].Name),
            cancellationToken);

        return Results.Ok(new { jobApplicationId = id, webhooks = sent });
    }

    private static async Task<IResult> DeleteJobApplicationAsync(
        string id,
        Store store,
        WebhookSender webhooks,
        CancellationToken cancellationToken)
    {
        if (!store.TryRemoveJobApplication(id, out var application))
        {
            return Results.NotFound($"Unknown job application {id}");
        }

        var sent = await webhooks.SendAsync(
            "job_application.destroy",
            TeamtailorJson.JobApplicationDestroyWebhookData(id, application.CandidateId),
            cancellationToken);

        return Results.Ok(new { jobApplicationId = id, webhooks = sent });
    }

    private static async Task<IResult> UpdateCandidateAsync(
        string id,
        CandidateChange change,
        Store store,
        WebhookSender webhooks,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!store.Candidates.TryGetValue(id, out var candidate))
        {
            return Results.NotFound($"Unknown candidate {id}");
        }

        var changed = candidate with
        {
            FirstName = change.FirstName ?? candidate.FirstName,
            LastName = change.LastName ?? candidate.LastName,
            Email = change.Email ?? candidate.Email,
            Phone = change.Phone ?? candidate.Phone,
            ResumeText = change.ResumeText ?? candidate.ResumeText,
        };

        if (changed == candidate)
        {
            return Results.Ok(new { candidateId = id, webhooks = Array.Empty<WebhookDelivery>() });
        }

        // Real Teamtailor fires candidate.update only for selected fields (e.g. email, phone; the full list is not
        // documented). The fake fires for any change, so a test can trigger an update with any field.
        changed = changed with { UpdatedAt = timeProvider.GetUtcNow() };
        store.Candidates[id] = changed;
        var sent = await webhooks.SendAsync(
            "candidate.update", TeamtailorJson.CandidateWebhookData(id, changed), cancellationToken);

        return Results.Ok(new { candidateId = id, webhooks = sent });
    }

    private static async Task<IResult> DeleteCandidateAsync(
        string id,
        Store store,
        WebhookSender webhooks,
        CancellationToken cancellationToken)
    {
        if (!store.Candidates.TryRemove(id, out _))
        {
            return Results.NotFound($"Unknown candidate {id}");
        }

        // Applications go with the candidate, so /v1 answers 404 for them too. Whether Teamtailor also sends
        // job_application.destroy for each of them is not documented; the fake sends only candidate.destroy.
        foreach (var application in store.JobApplications.Where(a => a.Value.CandidateId == id))
        {
            store.TryRemoveJobApplication(application.Key, out _);
        }

        var sent = await webhooks.SendAsync(
            "candidate.destroy", TeamtailorJson.CandidateDestroyWebhookData(id), cancellationToken);

        return Results.Ok(new { candidateId = id, webhooks = sent });
    }
}
