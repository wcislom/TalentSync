using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Matching.Application.UseCases;

/// <summary>
/// Creates or updates the consultant profile when a job application is in the qualified stage (D2, D3), with the
/// LLM's suggestion when there is a valid one (D5, ADR 007).
/// </summary>
/// <remarks>
/// Takes the application id, not the event type: the fetched state decides (notify-then-fetch, ADR 003), so a
/// duplicate or reordered event repeats an idempotent upsert. An application moved back from qualified, or deleted,
/// leaves the profile as it is: the Matching Platform owns it (D4).
/// </remarks>
public sealed class CreateConsultantProfileUseCase(
    IRecruitmentSource recruitmentSource,
    IEnrichmentService enrichmentService,
    IEnrichmentResultStore enrichmentResults,
    IMatchingPlatform matchingPlatform)
{
    public async Task<CreateConsultantProfileResult> ExecuteAsync(string jobApplicationId, CancellationToken cancellationToken)
    {
        var application = await recruitmentSource.GetJobApplicationAsync(jobApplicationId, cancellationToken);

        switch (application)
        {
            case FetchResult<JobApplication>.Found { Value.Status: ApplicationStatus.Qualified } found:
                // The candidate id comes from the source, not from the event: the API state is the truth.
                return await CreateProfileAsync(found.Value.CandidateId, cancellationToken);

            case FetchResult<JobApplication>.Found or FetchResult<JobApplication>.Deleted:
                return CreateConsultantProfileResult.Skipped;

            case FetchResult<JobApplication>.Rejected rejected:
                return CreateConsultantProfileResult.Rejected(rejected.Reason);

            default:
                throw new InvalidOperationException("Unknown fetch result.");
        }
    }

    private async Task<CreateConsultantProfileResult> CreateProfileAsync(string candidateId, CancellationToken cancellationToken)
    {
        var candidate = await recruitmentSource.GetCandidateAsync(candidateId, cancellationToken);

        switch (candidate)
        {
            case FetchResult<Candidate>.Found found:
                var id = new CandidateId(candidateId);
                var enrichment = await EnrichAsync(id, found.Value.CvText, cancellationToken);
                var profile = enrichment is null
                    ? ConsultantProfile.NotEnriched(id)
                    : ConsultantProfile.Enriched(id, enrichment);
                return await UpsertAsync(profile, cancellationToken);

            // Deleted between the two reads: there is no one to create a profile for.
            case FetchResult<Candidate>.Deleted:
                return CreateConsultantProfileResult.Skipped;

            case FetchResult<Candidate>.Rejected rejected:
                return CreateConsultantProfileResult.Rejected(rejected.Reason);

            default:
                throw new InvalidOperationException("Unknown fetch result.");
        }
    }

    /// <summary>The suggestion for this CV, or null when there is none (D5: the profile is created anyway).</summary>
    private async Task<Enrichment?> EnrichAsync(CandidateId candidateId, string? cvText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cvText))
        {
            return null;
        }

        var cvHash = CvHash.Of(cvText);

        // A crash or a Matching Platform failure after the save below is retried by the handler loop, and the retry
        // finds the suggestion here instead of asking the model again for a possibly different one (invariant 5).
        var stored = await enrichmentResults.FindAsync(candidateId, cvHash, cancellationToken);
        if (stored is not null)
        {
            return stored;
        }

        return await enrichmentService.EnrichAsync(cvText, cancellationToken) switch
        {
            // Saved before the upsert; the save returns what is stored, which differs from ours when another processor
            // enriched the same CV first.
            EnrichmentOutcome.Enriched enriched =>
                await enrichmentResults.SaveAsync(candidateId, cvHash, enriched.Enrichment, enriched.ModelId, cancellationToken),
            EnrichmentOutcome.Failed => null,
            _ => throw new InvalidOperationException("Unknown enrichment outcome."),
        };
    }

    private async Task<CreateConsultantProfileResult> UpsertAsync(ConsultantProfile profile, CancellationToken cancellationToken) =>
        await matchingPlatform.UpsertProfileAsync(profile, cancellationToken) switch
        {
            MatchingPlatformResult.DoneResult => CreateConsultantProfileResult.Upserted,
            MatchingPlatformResult.RejectedResult rejected => CreateConsultantProfileResult.Rejected(rejected.Reason),
            _ => throw new InvalidOperationException("Unknown Matching Platform result."),
        };
}
