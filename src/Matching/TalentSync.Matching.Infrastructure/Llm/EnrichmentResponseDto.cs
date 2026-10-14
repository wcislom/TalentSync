using System.ComponentModel;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>
/// The JSON the model is asked to return. Its schema is sent with the request; everything is nullable because the
/// answer is untrusted (invariant 16) and is validated by <c>Enrichment.TryCreate</c>, not by deserialisation.
/// </summary>
internal sealed record EnrichmentResponseDto(
    [property: Description("Technical skills named in the CV, each a short name such as \"C#\" or \"Kubernetes\".")]
    IReadOnlyList<string?>? Skills,
    [property: Description("One of: Junior, Mid, Senior, Lead.")]
    string? Seniority,
    [property: Description("How sure you are of this assessment, from 0 to 1.")]
    double? Confidence);
