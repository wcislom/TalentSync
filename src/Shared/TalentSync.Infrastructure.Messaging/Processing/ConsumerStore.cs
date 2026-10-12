using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;

namespace TalentSync.Infrastructure.Messaging.Processing;

/// <summary>
/// <see cref="IConsumerStore"/> over the worker's own <c>DbContext</c>, which applies
/// <c>InboxEntryConfiguration</c> and <c>ParkedEventConfiguration</c> with its schema.
/// </summary>
internal sealed class ConsumerStore<TDbContext>(IDbContextFactory<TDbContext> dbContextFactory) : IConsumerStore
    where TDbContext : DbContext
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public async Task<bool> IsHandledAsync(string source, string eventId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<InboxEntry>().AnyAsync(e => e.Source == source && e.EventId == eventId, cancellationToken);
    }

    public async Task MarkHandledAsync(InboxEntry entry, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Add(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException
                                                  {
                                                      Number: UniqueConstraintViolation or UniqueIndexViolation,
                                                  })
        {
            // Invariant 12: the unique index, not a lookup, decides that the event was already handled.
        }
    }

    public async Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Add(parkedEvent);
        await db.SaveChangesAsync(cancellationToken);
    }
}
