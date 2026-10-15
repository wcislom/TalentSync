using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentSync.Matching.Infrastructure.EnrichmentResults;

namespace TalentSync.Matching.Infrastructure.Persistence;

internal sealed class EnrichmentResultConfiguration : IEntityTypeConfiguration<EnrichmentResult>
{
    public void Configure(EntityTypeBuilder<EnrichmentResult> builder)
    {
        builder.ToTable("EnrichmentResults");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityColumn();

        builder.Property(r => r.CandidateId).HasMaxLength(100);
        // Hex SHA-256 of the CV text as fetched.
        builder.Property(r => r.CvHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(r => r.PromptVersion).HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.Result).HasColumnType("json");
        builder.Property(r => r.ModelId).HasMaxLength(100);
        builder.Property(r => r.CreatedAt).HasColumnType("datetime2");

        // The guard against two suggestions for one CV and prompt (invariant 12), never check-then-insert.
        builder.HasIndex(r => new { r.CandidateId, r.CvHash, r.PromptVersion })
            .IsUnique()
            .HasDatabaseName("UX_EnrichmentResults_CandidateId_CvHash_PromptVersion");
    }
}
