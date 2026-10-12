using System.Text.Json.Nodes;

namespace FakeTeamtailor;

/// <summary>
/// The fake Teamtailor REST API that the TalentSync ACL calls. Shapes come from <see cref="TeamtailorJson"/>.
/// </summary>
public static class ApiEndpoints
{
    private const string JsonApiContentType = "application/vnd.api+json";

    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").AddEndpointFilter(RequireApiHeaders).AddEndpointFilter(ChaosEndpoints.ApplyToApi);

        v1.MapGet("/candidates/{id}", (string id, Store store) =>
            store.Candidates.TryGetValue(id, out var candidate)
                ? JsonApi(new JsonObject { ["data"] = TeamtailorJson.CandidateResource(id, candidate) })
                : NotFound());

        v1.MapGet("/job-applications/{id}", (string id, string? include, Store store) =>
        {
            if (!store.JobApplications.TryGetValue(id, out var application))
            {
                return NotFound();
            }

            var document = new JsonObject { ["data"] = TeamtailorJson.JobApplicationResource(id, application) };
            var includesStage = include?.Split(',').Contains("stage") == true;
            if (includesStage && store.Stages.TryGetValue(application.StageId, out var stage))
            {
                document["included"] = new JsonArray(TeamtailorJson.StageResource(application.StageId, stage));
            }

            return JsonApi(document);
        });
    }

    // Both failures answer 401, never 400: the ACL treats 401 as a configuration problem that blocks the
    // partition until fixed, while 400 would park every event as permanently broken.
    // Whether real Teamtailor answers a missing X-Api-Version with 401 is not documented.
    private static async ValueTask<object?> RequireApiHeaders(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var options = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedAuthorization = $"Token token={options["FakeTeamtailor:ApiKey"]}";
        var expectedVersion = options["FakeTeamtailor:ApiVersion"];

        if (request.Headers.Authorization != expectedAuthorization)
        {
            return Error(StatusCodes.Status401Unauthorized, "Invalid or missing API key");
        }

        if (request.Headers["X-Api-Version"] != expectedVersion)
        {
            return Error(StatusCodes.Status401Unauthorized, $"Invalid or missing X-Api-Version, expected {expectedVersion}");
        }

        return await next(context);
    }

    private static IResult JsonApi(JsonObject document, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(document, contentType: JsonApiContentType, statusCode: statusCode);

    private static IResult NotFound() => Error(StatusCodes.Status404NotFound, "Record not found");

    private static IResult Error(int status, string title) =>
        JsonApi(new JsonObject
        {
            ["errors"] = new JsonArray(new JsonObject { ["status"] = status.ToString(), ["title"] = title }),
        }, status);
}
