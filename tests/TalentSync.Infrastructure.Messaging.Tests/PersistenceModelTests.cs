using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;
using TalentSync.Infrastructure.Messaging.Persistence;

namespace TalentSync.Infrastructure.Messaging.Tests;

public class PersistenceModelTests
{
    // Acceptance tests would not notice its removal: duplicates on one partition arrive in order and are skipped by
    // the inbox lookup. The index only matters when two processors handle the same event at once.
    [Fact]
    public void Inbox_HasUniqueIndexOnSourceAndEventId()
    {
        var index = Assert.Single(EntityType<InboxEntry>().GetIndexes());

        Assert.True(index.IsUnique);
        Assert.Equal([nameof(InboxEntry.Source), nameof(InboxEntry.EventId)], index.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ParkingLot_StoresEnvelopeAsText()
    {
        var envelope = EntityType<ParkedEvent>().FindProperty(nameof(ParkedEvent.Envelope))!;

        Assert.Equal("nvarchar(max)", envelope.GetColumnType());
    }

    private static IReadOnlyEntityType EntityType<T>()
    {
        var modelBuilder = SqlServerConventionSetBuilder.CreateModelBuilder();
        modelBuilder.ApplyConfiguration(new InboxEntryConfiguration("sync"));
        modelBuilder.ApplyConfiguration(new ParkedEventConfiguration("sync"));
        return modelBuilder.Model.FindEntityType(typeof(T))!;
    }
}
