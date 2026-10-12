using FakeMatchingPlatform.MatchingPlatform;
using FakeMatchingPlatform.State;

namespace FakeMatchingPlatform.Testing;

/// <summary>What tests and the demo read to see the end result. Not counted in <c>/calls</c> and never broken by anomalies.</summary>
public static class ProfileListEndpoints
{
    public static void MapProfileList(this IEndpointRouteBuilder app)
    {
        app.MapGet("/profiles", (Store store) =>
            store.Profiles
                .OrderBy(profile => profile.Key)
                .Select(profile => ProfileResponse.From(profile.Key, profile.Value)));

        app.MapGet("/profiles/{candidateId}", (string candidateId, Store store) =>
            store.Profiles.TryGetValue(candidateId, out var profile)
                ? Results.Ok(ProfileResponse.From(candidateId, profile))
                : Results.NotFound());
    }
}
