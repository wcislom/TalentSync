using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;
using TalentSync.Matching.Infrastructure.EnrichmentResults;

namespace TalentSync.Matching.Infrastructure.Persistence;

internal sealed class EnrichmentResultStore(IDbContextFactory<MatchingDbContext> dbContextFactory, TimeProvider timeProvider)
    : IEnrichmentResultStore
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public async Task<Enrichment?> FindAsync(
        CandidateId candidateId, CvHash cvHash, string promptVersion, CancellationToken cancellationToken)
    {
        var json = await FindJsonAsync(candidateId, cvHash, promptVersion, cancellationToken);
        return json is null ? null : StoredEnrichment.FromJson(json);
    }

    public async Task<Enrichment> SaveAsync(
        CandidateId candidateId, CvHash cvHash, string promptVersion, Enrichment enrichment, string modelId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.EnrichmentResults.Add(new EnrichmentResult
        {
            CandidateId = candidateId.Value,
            CvHash = cvHash.Value,
            PromptVersion = promptVersion,
            Result = StoredEnrichment.ToJson(enrichment),
            ModelId = modelId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return enrichment;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: UniqueConstraintViolation or UniqueIndexViolation })
        {
            // Another processor enriched the same CV with the same prompt first (e.g. after a partition moved mid-event). Its answer wins,
            // so both upserts send the same suggestion.
            var stored = await FindJsonAsync(candidateId, cvHash, promptVersion, cancellationToken)
                ?? throw new InvalidOperationException("Duplicate enrichment result reported, but no stored row found.");
            return StoredEnrichment.FromJson(stored);
        }
    }

    private async Task<string?> FindJsonAsync(
        CandidateId candidateId, CvHash cvHash, string promptVersion, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.EnrichmentResults
            .AsNoTracking()
            .Where(r => r.CandidateId == candidateId.Value && r.CvHash == cvHash.Value && r.PromptVersion == promptVersion)
            .Select(r => r.Result)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
