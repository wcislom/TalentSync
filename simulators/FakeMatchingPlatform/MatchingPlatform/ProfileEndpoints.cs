using FakeMatchingPlatform.State;
using FakeMatchingPlatform.Testing;

namespace FakeMatchingPlatform.MatchingPlatform;

public sealed record ProfileRequest(
    IReadOnlyList<string>? Skills, Seniority? Seniority, double? Confidence, EnrichmentStatus? EnrichmentStatus);

public sealed record ProfileResponse(
    string CandidateId,
    IReadOnlyList<string> Skills,
    Seniority? Seniority,
    double? Confidence,
    EnrichmentStatus EnrichmentStatus,
    DateTimeOffset UpdatedAt)
{
    public static ProfileResponse From(string candidateId, Profile profile) =>
        new(candidateId, profile.Skills, profile.Seniority, profile.Confidence, profile.EnrichmentStatus, profile.UpdatedAt);
}

/// <summary>The fake Matching Platform API that Matching.Worker calls. Keyed by candidate id (D3), so a repeated call is harmless.</summary>
public static class ProfileEndpoints
{
    public static void MapProfiles(this IEndpointRouteBuilder app) =>
        app.MapGroup("/profiles")
            .AddEndpointFilter(AnomalyEndpoints.ApplyToApi)
            .MapPut("/{candidateId}", Upsert);

    // 201 on create and 200 on update, so a demo shows which one a redelivered event hit; the client must
    // treat both as success. An unknown enrichment status or seniority string fails JSON binding with 400 before this runs.
    // Last write wins: a NotEnriched PUT replaces an Enriched profile, the fake has no opinion on that.
    private static IResult Upsert(string candidateId, ProfileRequest request, Store store, TimeProvider timeProvider)
    {
        var errors = new List<string>();
        if (request.EnrichmentStatus is null)
        {
            errors.Add("enrichmentStatus is required");
        }

        // A NotEnriched profile has no suggestion to carry (D5), so only Enriched needs seniority and confidence.
        if (request.EnrichmentStatus == EnrichmentStatus.Enriched)
        {
            if (request.Seniority is null)
            {
                errors.Add("seniority is required when enrichmentStatus is Enriched");
            }

            if (request.Confidence is null)
            {
                errors.Add("confidence is required when enrichmentStatus is Enriched");
            }
        }

        if (request.Confidence is < 0 or > 1)
        {
            errors.Add("confidence must be between 0 and 1");
        }

        if (errors.Count > 0)
        {
            return Results.Problem(string.Join("; ", errors), statusCode: StatusCodes.Status400BadRequest);
        }

        var profile = new Profile(
            request.Skills ?? [], request.Seniority, request.Confidence, request.EnrichmentStatus!.Value, timeProvider.GetUtcNow());
        var created = true;
        store.Profiles.AddOrUpdate(candidateId, profile, (_, _) =>
        {
            created = false;
            return profile;
        });

        var response = ProfileResponse.From(candidateId, profile);
        return created ? Results.Created($"/profiles/{candidateId}", response) : Results.Ok(response);
    }
}
