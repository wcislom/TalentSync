using System.Text.Json;
using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Infrastructure.EnrichmentResults;

/// <summary>The shape of <see cref="EnrichmentResult.Result"/>.</summary>
internal sealed record StoredEnrichment(IReadOnlyList<string> Skills, string Seniority, double Confidence)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ToJson(Enrichment enrichment) =>
        JsonSerializer.Serialize(
            new StoredEnrichment([.. enrichment.Skills.Select(s => s.Name)], enrichment.Seniority.ToString(), enrichment.Confidence.Value),
            JsonOptions);

    // Through the constructors, not Enrichment.TryCreate: a stored row was valid when it was written, so a broken rule
    // here is our bug and should fail loudly instead of turning into NotEnriched.
    public static Enrichment FromJson(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredEnrichment>(json, JsonOptions)
            ?? throw new InvalidOperationException("Stored enrichment is empty.");
        return new Enrichment(
            stored.Skills.Select(name => new Skill(name)),
            Enum.Parse<Seniority>(stored.Seniority),
            new Confidence(stored.Confidence));
    }
}
