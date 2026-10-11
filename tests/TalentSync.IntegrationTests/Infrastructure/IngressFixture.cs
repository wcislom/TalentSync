using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Ingress.Api.Persistence;
using Testcontainers.EventHubs;
using Testcontainers.MsSql;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>
/// SQL Server and the Event Hubs emulator (with its Azurite) in containers, Ingress.Api in process.
/// Started once for the <see cref="IngressCollection"/>.
/// </summary>
public sealed class IngressFixture : IAsyncLifetime
{
    public const string HubName = "ats-events";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    // Must match deploy/eventhubs/Config.json: the module builds its own emulator configuration.
    private readonly EventHubsContainer _eventHubs = new EventHubsBuilder("mcr.microsoft.com/azure-messaging/eventhubs-emulator:2.2.1")
        .WithAcceptLicenseAgreement(true)
        .WithConfigurationBuilder(EventHubsServiceConfiguration.Create().WithEntity(HubName, 4, "sync-worker", "matching-worker"))
        .Build();

    private WebApplicationFactory<Program>? _ingress;

    public HttpClient Ingress { get; private set; } = null!;

    public HubReader Hub { get; private set; } = null!;

    /// <summary>The emulator container, for scenarios that make Event Hubs unavailable.</summary>
    public EventHubsContainer EventHubs => _eventHubs;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _eventHubs.StartAsync());

        var sqlConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "TalentSync" }.ConnectionString;
        var eventHubsConnectionString = _eventHubs.GetConnectionString();

        // Migrate before the host starts, so the relay never polls a missing table.
        await using (var db = CreateDbContext(sqlConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        _ingress = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("ConnectionStrings:Sql", sqlConnectionString)
            .UseSetting("EventHubs:ConnectionString", eventHubsConnectionString)
            .UseSetting("EventHubs:HubName", HubName)
            // A send to an unavailable hub must fail within seconds, not after the SDK's 4 × 60 s,
            // so that scenarios can observe the relay's failure path.
            .UseSetting("EventHubs:TryTimeout", "00:00:02")
            .UseSetting("EventHubs:MaxRetries", "0")
            .UseSetting("OutboxRelay:MaxBackoff", "00:00:04"));

        Ingress = _ingress.CreateClient();
        Hub = new HubReader(eventHubsConnectionString, HubName);
    }

    public async Task<TResult> QueryIngressDbAsync<TResult>(Func<IngressDbContext, Task<TResult>> query)
    {
        await using var scope = _ingress!.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IngressDbContext>());
    }

    /// <summary>
    /// Unpublished outbox rows of the candidate that the relay could claim right now. A row the relay holds during a
    /// send is locked, and the relay's own claim (UPDLOCK, READPAST) skips it, so it is not counted.
    /// </summary>
    public Task<int> CountClaimableOutboxRowsAsync(string candidateId) =>
        QueryIngressDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var claimable = await db.Database
                .SqlQuery<int>($"""
                    SELECT COUNT(*) AS [Value]
                    FROM [ingress].[Outbox] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE [CandidateId] = {candidateId} AND [PublishedAt] IS NULL
                    """)
                .SingleAsync();
            await transaction.RollbackAsync();
            return claimable;
        });

    public async ValueTask DisposeAsync()
    {
        if (_ingress is not null)
        {
            await _ingress.DisposeAsync();
        }

        await _eventHubs.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private static IngressDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<IngressDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IngressDbContext.Schema))
            .Options);
}

[CollectionDefinition(Name)]
public sealed class IngressCollection : ICollectionFixture<IngressFixture>
{
    public const string Name = "Ingress";
}
