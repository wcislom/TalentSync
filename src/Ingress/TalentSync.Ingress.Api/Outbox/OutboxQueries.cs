using Microsoft.EntityFrameworkCore;
using TalentSync.Ingress.Api.Persistence;

namespace TalentSync.Ingress.Api.Outbox;

/// <summary>The relay's raw SQL, kept in one place (persistence rule).</summary>
internal static class OutboxQueries
{
    /// <summary>
    /// Locks up to <paramref name="batchSize"/> unpublished rows, in <see cref="OutboxMessage.Id"/> order, until the
    /// current transaction ends. The rows are tracked, so setting <see cref="OutboxMessage.PublishedAt"/> and saving
    /// marks them.
    /// </summary>
    /// <remarks>
    /// UPDLOCK reserves the rows for this transaction, ROWLOCK keeps the locks per row, READPAST skips rows locked by
    /// someone else instead of waiting. READPAST also skips a row whose insert is not committed yet, so a later row can
    /// be published before an earlier one; notify-then-fetch makes that harmless (ADR 003).
    /// </remarks>
    /// <exception cref="InvalidOperationException">No transaction is active.</exception>
    public static Task<List<OutboxMessage>> ClaimUnpublishedAsync(
        this IngressDbContext db,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Without a transaction SQL Server releases the UPDLOCK right after the read: the query still returns rows,
        // but nothing reserves them. That bug would be silent until two relays published the same row.
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Claiming outbox rows requires a transaction: the locks last until it ends.");
        }

        return db.Outbox
            .FromSql($"""
                SELECT TOP ({batchSize}) *
                FROM [ingress].[Outbox] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [PublishedAt] IS NULL
                ORDER BY [Id]
                """)
            .ToListAsync(cancellationToken);
    }
}
