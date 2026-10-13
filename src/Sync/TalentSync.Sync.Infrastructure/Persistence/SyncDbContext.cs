using Microsoft.EntityFrameworkCore;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;
using TalentSync.Infrastructure.Messaging.Persistence;

namespace TalentSync.Sync.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>sync</c> schema. Matching.Worker has its own <c>Inbox</c>: a shared one would make it skip every event
/// Sync.Worker already handled (invariant 4).
/// </summary>
public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public const string Schema = "sync";

    public DbSet<InboxEntry> Inbox => Set<InboxEntry>();

    public DbSet<ParkedEvent> ParkingLot => Set<ParkedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new InboxEntryConfiguration(Schema));
        modelBuilder.ApplyConfiguration(new ParkedEventConfiguration(Schema));
    }
}
