extern alias FakeCrm;
extern alias FakeTeamtailor;
extern alias SyncWorker;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Ingress.Api.Persistence;
using TalentSync.Sync.Infrastructure.Persistence;
using Testcontainers.Azurite;
using Testcontainers.EventHubs;
using Testcontainers.MsSql;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>
/// The whole system for the <see cref="SystemCollection"/>: SQL Server, the Event Hubs emulator and its Azurite in
/// containers; Ingress.Api, Sync.Worker, FakeTeamtailor and FakeCrm in process, wired to each other by
/// <see cref="HostRouter"/>. Started once; scenarios stay independent through their own candidate ids.
/// </summary>
public sealed class SystemFixture : IAsyncLifetime
{
    public const string HubName = "ats-events";

    private const string AzuriteAlias = "azurite";
    private const string IngressHost = "ingress.test";
    private const string TeamtailorHost = "teamtailor.test";
    private const string CrmHost = "crm.test";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
    private readonly AzuriteContainer _azurite;
    private readonly EventHubsContainer _eventHubs;

    private WebApplicationFactory<Program>? _ingress;
    private WebApplicationFactory<FakeTeamtailor::Program>? _fakeTeamtailor;
    private WebApplicationFactory<FakeCrm::Program>? _fakeCrm;
    private WebApplicationFactory<SyncWorker::Program>? _syncWorker;

    public SystemFixture()
    {
        // The emulator's own Azurite, also used by the workers for checkpoints, as in deploy/docker-compose.yml.
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
            .WithNetwork(_network)
            .WithNetworkAliases(AzuriteAlias)
            .Build();

        // Must match deploy/eventhubs/Config.json: the module builds its own emulator configuration.
        _eventHubs = new EventHubsBuilder("mcr.microsoft.com/azure-messaging/eventhubs-emulator:2.2.1")
            .WithAcceptLicenseAgreement(true)
            .WithAzuriteContainer(_network, _azurite, AzuriteAlias)
            .WithConfigurationBuilder(EventHubsServiceConfiguration.Create().WithEntity(HubName, 4, "sync-worker", "matching-worker"))
            .Build();
    }

    public HttpClient Ingress { get; private set; } = null!;

    public HubReader Hub { get; private set; } = null!;

    /// <summary>The emulator container, for scenarios that make Event Hubs unavailable.</summary>
    public EventHubsContainer EventHubs => _eventHubs;

    public FakeTeamtailorTestClient FakeTeamtailor { get; private set; } = null!;

    public FakeCrmTestClient FakeCrm { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _network.CreateAsync();
        await Task.WhenAll(_sql.StartAsync(), StartEventHubsAsync());

        var sqlConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "TalentSync" }.ConnectionString;
        var eventHubsConnectionString = _eventHubs.GetConnectionString();

        // Migrate before the hosts start, so neither the relay nor a worker meets a missing table.
        await using (var db = CreateIngressDbContext(sqlConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        await using (var db = CreateSyncDbContext(sqlConnectionString))
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

        _fakeTeamtailor = new WebApplicationFactory<FakeTeamtailor::Program>().WithWebHostBuilder(host => host
            .UseSetting("FakeTeamtailor:WebhookUrl", $"http://{IngressHost}/webhooks/teamtailor")
            .ConfigureTestServices(services => RouteHttpClients(services, new Dictionary<string, TestServer>
            {
                [IngressHost] = _ingress.Server,
            })));
        FakeTeamtailor = new FakeTeamtailorTestClient(_fakeTeamtailor.CreateClient());

        _fakeCrm = new WebApplicationFactory<FakeCrm::Program>();
        FakeCrm = new FakeCrmTestClient(_fakeCrm.CreateClient());

        _syncWorker = new WebApplicationFactory<SyncWorker::Program>().WithWebHostBuilder(host => host
            .UseSetting("ConnectionStrings:Sql", sqlConnectionString)
            .UseSetting("EventHubs:ConnectionString", eventHubsConnectionString)
            .UseSetting("Checkpoints:BlobConnectionString", _azurite.GetConnectionString())
            .UseSetting("Teamtailor:BaseUrl", $"http://{TeamtailorHost}")
            .UseSetting("Teamtailor:ApiKey", "fake-api-key")
            .UseSetting("Crm:BaseUrl", $"http://{CrmHost}")
            // Outages in scenarios last seconds; the production cap of 30 s would only make them slower.
            .UseSetting("Consumer:MaxBackoff", "00:00:05")
            .ConfigureTestServices(services => RouteHttpClients(services, new Dictionary<string, TestServer>
            {
                [TeamtailorHost] = _fakeTeamtailor.Server,
                [CrmHost] = _fakeCrm.Server,
            })));

        // A worker has no request to trigger its start: touching the server starts the host and its event processor.
        _ = _syncWorker.Server;

        Hub = new HubReader(eventHubsConnectionString, HubName);
    }

    public async Task<TResult> QueryIngressDbAsync<TResult>(Func<IngressDbContext, Task<TResult>> query)
    {
        await using var scope = _ingress!.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IngressDbContext>());
    }

    public async Task<TResult> QuerySyncDbAsync<TResult>(Func<SyncDbContext, Task<TResult>> query)
    {
        var factory = _syncWorker!.Services.GetRequiredService<IDbContextFactory<SyncDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await query(db);
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
        // Workers first: they consume what the others produce.
        foreach (var host in new IAsyncDisposable?[] { _syncWorker, _fakeCrm, _fakeTeamtailor, _ingress })
        {
            if (host is not null)
            {
                await host.DisposeAsync();
            }
        }

        await _eventHubs.DisposeAsync();
        await _azurite.DisposeAsync();
        await _sql.DisposeAsync();
        await _network.DisposeAsync();
    }

    private async Task StartEventHubsAsync()
    {
        await _azurite.StartAsync();
        await _eventHubs.StartAsync();
    }

    private static void RouteHttpClients(IServiceCollection services, IReadOnlyDictionary<string, TestServer> servers) =>
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => new HostRouter(servers)));

    private static IngressDbContext CreateIngressDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<IngressDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IngressDbContext.Schema))
            .Options);

    private static SyncDbContext CreateSyncDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<SyncDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SyncDbContext.Schema))
            .Options);
}

[CollectionDefinition(Name)]
public sealed class SystemCollection : ICollectionFixture<SystemFixture>
{
    public const string Name = "System";
}
