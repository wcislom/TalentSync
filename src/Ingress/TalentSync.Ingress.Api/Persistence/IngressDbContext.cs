using Microsoft.EntityFrameworkCore;
using TalentSync.Ingress.Api.Outbox;

namespace TalentSync.Ingress.Api.Persistence;

/// <summary>Owns the <c>ingress</c> schema. No other host registers it (invariant 4).</summary>
public sealed class IngressDbContext(DbContextOptions<IngressDbContext> options) : DbContext(options)
{
    public const string Schema = "ingress";

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
