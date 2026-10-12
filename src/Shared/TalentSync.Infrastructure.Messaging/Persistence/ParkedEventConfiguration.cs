using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentSync.Infrastructure.Messaging.ParkingLot;

namespace TalentSync.Infrastructure.Messaging.Persistence;

/// <summary>Applied by each worker's <c>DbContext</c> with its own schema: same shape, separate tables (invariant 4).</summary>
public sealed class ParkedEventConfiguration(string schema) : IEntityTypeConfiguration<ParkedEvent>
{
    public void Configure(EntityTypeBuilder<ParkedEvent> builder)
    {
        builder.ToTable("ParkingLot", schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityColumn();

        builder.Property(e => e.Source).HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.EventId).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(e => e.PartitionId).HasMaxLength(32).IsUnicode(false);

        // Not the json type: an unreadable body is often not valid JSON, so SQL Server would reject the insert and
        // the event would be neither handled nor parked.
        builder.Property(e => e.Envelope).HasColumnType("nvarchar(max)");

        // Unbounded for the same reason: a long exception message must never make the parking insert fail.
        builder.Property(e => e.Error).HasColumnType("nvarchar(max)");
        builder.Property(e => e.ParkedAt).HasColumnType("datetime2");
    }
}
