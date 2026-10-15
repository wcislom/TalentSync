using Microsoft.Extensions.AI;
using TalentSync.Matching.Infrastructure.Llm;

namespace TalentSync.Matching.Infrastructure.Tests;

public sealed class EnrichmentPromptTests
{
    [Fact]
    public void Version_FitsThePromptVersionColumn()
    {
        Assert.Matches("^[0-9a-f]{16}$", EnrichmentPrompt.Version);
    }

    [Fact]
    public void Version_IsTheSameForTheSamePrompt()
    {
        Assert.Equal(
            EnrichmentPrompt.Version,
            EnrichmentPrompt.VersionOf(EnrichmentPrompt.Instructions, EnrichmentPrompt.ResponseFormat));
    }

    [Fact]
    public void Version_ChangesWithTheInstructions()
    {
        Assert.NotEqual(
            EnrichmentPrompt.Version,
            EnrichmentPrompt.VersionOf(EnrichmentPrompt.Instructions + " Be brief.", EnrichmentPrompt.ResponseFormat));
    }

    [Fact]
    public void Version_ChangesWithTheSchema()
    {
        var otherSchema = ChatResponseFormat.ForJsonSchema<OtherAnswer>(
            EnrichmentPrompt.JsonOptions, EnrichmentPrompt.ResponseFormat.SchemaName,
            EnrichmentPrompt.ResponseFormat.SchemaDescription);

        Assert.NotEqual(
            EnrichmentPrompt.Version,
            EnrichmentPrompt.VersionOf(EnrichmentPrompt.Instructions, otherSchema));
    }

    private sealed record OtherAnswer(string Seniority);
}
