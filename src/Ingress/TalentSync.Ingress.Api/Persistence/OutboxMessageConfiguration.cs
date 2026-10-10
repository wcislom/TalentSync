using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentSync.Ingress.Api.Outbox;

namespace TalentSync.Ingress.Api.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("Outbox");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).UseIdentityColumn();

        // Hex SHA-256 of the raw webhook body.
        builder.Property(m => m.EventId).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(m => m.CandidateId).HasMaxLength(100);
        builder.Property(m => m.Type).HasMaxLength(100);
        builder.Property(m => m.Envelope).HasColumnType("json");
        builder.Property(m => m.CreatedAt).HasColumnType("datetime2");
        builder.Property(m => m.PublishedAt).HasColumnType("datetime2");

        // The relay reads only unpublished rows in Id order; published rows drop out of the index.
        builder.HasIndex(m => m.Id)
            .HasDatabaseName("IX_Outbox_Unpublished")
            .HasFilter("[PublishedAt] IS NULL");
    }
}
