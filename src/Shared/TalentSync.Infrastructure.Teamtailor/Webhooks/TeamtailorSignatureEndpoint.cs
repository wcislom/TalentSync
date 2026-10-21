using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

public static class TeamtailorSignatureEndpoint
{
    /// <summary>Answers 401 before the handler runs unless the <c>TT-Signature</c> header signs the raw body.</summary>
    public static RouteHandlerBuilder RequireTeamtailorSignature(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<TeamtailorSignatureFilter>();
}
