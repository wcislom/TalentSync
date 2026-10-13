using Microsoft.EntityFrameworkCore;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;
using TalentSync.Infrastructure.Messaging.Persistence;

namespace TalentSync.Matching.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>matching</c> schema. Sync.Worker has its own <c>Inbox</c>: a shared one would make this worker skip
/// every event Sync.Worker already handled (invariant 4).
/// </summary>
public sealed class MatchingDbContext(DbContextOptions<MatchingDbContext> options) : DbContext(options)
{
    public const string Schema = "matching";

    public DbSet<InboxEntry> Inbox => Set<InboxEntry>();

    public DbSet<ParkedEvent> ParkingLot => Set<ParkedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new InboxEntryConfiguration(Schema));
        modelBuilder.ApplyConfiguration(new ParkedEventConfiguration(Schema));
    }
}
