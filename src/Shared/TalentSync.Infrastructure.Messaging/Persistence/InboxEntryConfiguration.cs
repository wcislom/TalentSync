using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentSync.Infrastructure.Messaging.Inbox;

namespace TalentSync.Infrastructure.Messaging.Persistence;

/// <summary>Applied by each worker's <c>DbContext</c> with its own schema: same shape, separate tables (invariant 4).</summary>
public sealed class InboxEntryConfiguration(string schema) : IEntityTypeConfiguration<InboxEntry>
{
    public void Configure(EntityTypeBuilder<InboxEntry> builder)
    {
        builder.ToTable("Inbox", schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityColumn();

        builder.Property(e => e.Source).HasMaxLength(50).IsUnicode(false);
        // Hex SHA-256 of the raw webhook body, as in ingress.Outbox.
        builder.Property(e => e.EventId).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(e => e.ProcessedAt).HasColumnType("datetime2");

        // The guard against duplicates (invariant 12), never check-then-insert.
        builder.HasIndex(e => new { e.Source, e.EventId })
            .IsUnique()
            .HasDatabaseName("UX_Inbox_Source_EventId");
    }
}
