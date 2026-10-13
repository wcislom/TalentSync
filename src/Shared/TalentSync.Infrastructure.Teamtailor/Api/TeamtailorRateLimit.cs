using System.Globalization;
using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace TalentSync.Infrastructure.Teamtailor.Api;

/// <summary>
/// How the API client's HTTP pipeline recognises Teamtailor's rate limit. Teamtailor sends no <c>Retry-After</c>,
/// only <c>X-Rate-Limit-Reset</c> (seconds until the window resets), so 429 needs its own retry.
/// </summary>
internal static class TeamtailorRateLimit
{
    internal const string ResetHeader = "X-Rate-Limit-Reset";

    /// <summary>
    /// The standard predicate without 429: the standard retry would call again inside the window, and 429s would
    /// open the circuit.
    /// </summary>
    public static bool IsTransientExceptRateLimit(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
        && HttpClientResiliencePredicates.IsTransient(outcome);

    // FakeTeamtailor rounds the reset up. If the real API rounds down, the retry can arrive a moment early and get a
    // second 429; the handler loop then waits it out.
    public static bool TryGetReset(HttpResponseMessage? response, out TimeSpan reset)
    {
        reset = TimeSpan.Zero;
        if (response is not { StatusCode: HttpStatusCode.TooManyRequests }
            || !response.Headers.TryGetValues(ResetHeader, out var values)
            || !int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        reset = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
