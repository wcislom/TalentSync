using System.Text.Json;

namespace FakeTeamtailor;

/// <summary>
/// The fake Teamtailor REST API that the TalentSync ACL calls. JSON:API with kebab-case attribute keys.
/// </summary>
public static class ApiEndpoints
{
    private const string JsonApiContentType = "application/vnd.api+json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
    };

    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").AddEndpointFilter(RequireApiHeaders);

        v1.MapGet("/candidates/{id}", (string id, Store store) =>
            store.Candidates.TryGetValue(id, out var candidate)
                ? JsonApi(new { data = ToResource(id, candidate) })
                : NotFound());

        v1.MapGet("/job-applications/{id}", (string id, string? include, Store store) =>
        {
            if (!store.JobApplications.TryGetValue(id, out var application))
            {
                return NotFound();
            }

            var data = ToResource(id, application);
            var includesStage = include?.Split(',').Contains("stage") == true;
            return includesStage && store.Stages.TryGetValue(application.StageId, out var stage)
                ? JsonApi(new { data, included = new[] { ToResource(application.StageId, stage) } })
                : JsonApi(new { data });
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

    private static object ToResource(string id, Candidate candidate) => new
    {
        id,
        type = "candidates",
        attributes = new
        {
            candidate.FirstName,
            candidate.LastName,
            candidate.Email,
            candidate.Phone,
            candidate.ResumeText,
            candidate.CreatedAt,
            candidate.UpdatedAt,
        },
    };

    private static object ToResource(string id, JobApplication application) => new
    {
        id,
        type = "job-applications",
        attributes = new
        {
            application.CreatedAt,
            application.UpdatedAt,
            application.RejectedAt,
        },
        relationships = new
        {
            candidate = Relationship(application.CandidateId, "candidates"),
            job = Relationship(application.JobId, "jobs"),
            stage = Relationship(application.StageId, "stages"),
        },
    };

    private static object ToResource(string id, Stage stage) => new
    {
        id,
        type = "stages",
        attributes = new { stage.Name },
    };

    private static object Relationship(string id, string type) => new { data = new { id, type } };

    private static IResult JsonApi(object document, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(document, Json, JsonApiContentType, statusCode);

    private static IResult NotFound() => Error(StatusCodes.Status404NotFound, "Record not found");

    private static IResult Error(int status, string title) =>
        JsonApi(new { errors = new[] { new { status = status.ToString(), title } } }, status);
}
