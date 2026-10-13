namespace TalentSync.Matching.Domain;

/// <summary>
/// Outcome of <see cref="EnrichmentSuggestion.Validate"/>. <see cref="InvalidResult.Reason"/> names the broken rule
/// only, never the model output (invariant 14).
/// </summary>
public abstract record EnrichmentSuggestionValidation
{
    private EnrichmentSuggestionValidation()
    {
    }

    public static EnrichmentSuggestionValidation Valid(EnrichmentSuggestion suggestion) => new ValidResult(suggestion);

    public static EnrichmentSuggestionValidation Invalid(string reason) => new InvalidResult(reason);

    public sealed record ValidResult(EnrichmentSuggestion Suggestion) : EnrichmentSuggestionValidation;

    public sealed record InvalidResult(string Reason) : EnrichmentSuggestionValidation;
}
