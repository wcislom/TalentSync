extern alias FakeCrm;
extern alias FakeMatchingPlatform;
extern alias FakeTeamtailor;
extern alias MatchingWorker;
extern alias SyncWorker;

using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Ingress.Api.Persistence;
using TalentSync.Matching.Infrastructure.Llm.Fake;
using TalentSync.Matching.Infrastructure.Persistence;
using TalentSync.Sync.Infrastructure.Persistence;
using Testcontainers.Azurite;
using Testcontainers.EventHubs;
using Testcontainers.MsSql;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>
/// The whole system for the <see cref="SystemCollection"/>: SQL Server, the Event Hubs emulator and its Azurite in
/// containers; Ingress.Api, both workers and the three simulators in process, wired to each other by
/// <see cref="HostRouter"/>. Started once; scenarios stay independent through their own candidate ids.
/// </summary>
public sealed class SystemFixture : IAsyncLifetime
{
    public const string HubName = "ats-events";

    private const string AzuriteAlias = "azurite";
    private const string IngressHost = "ingress.test";
    private const string TeamtailorHost = "teamtailor.test";
    private const string CrmHost = "crm.test";
    private const string MatchingPlatformHost = "matching.test";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
    private readonly AzuriteContainer _azurite;
    private readonly EventHubsContainer _eventHubs;

    private WebApplicationFactory<Program>? _ingress;
    private WebApplicationFactory<FakeTeamtailor::Program>? _fakeTeamtailor;
    private WebApplicationFactory<FakeCrm::Program>? _fakeCrm;
    private WebApplicationFactory<FakeMatchingPlatform::Program>? _fakeMatchingPlatform;
    private WebApplicationFactory<SyncWorker::Program>? _syncWorker;
    private WebApplicationFactory<MatchingWorker::Program>? _matchingWorker;

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

    public FakeMatchingPlatformTestClient FakeMatchingPlatform { get; private set; } = null!;

    /// <summary>How far Sync.Worker has processed each partition.</summary>
    public CheckpointReader SyncCheckpoints { get; private set; } = null!;

    /// <summary>How far Matching.Worker has processed each partition.</summary>
    public CheckpointReader MatchingCheckpoints { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _network.CreateAsync();
        await Task.WhenAll(_sql.StartAsync(), StartEventHubsAsync());

        var sqlConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "TalentSync" }.ConnectionString;
        var eventHubsConnectionString = _eventHubs.GetConnectionString();

        // Migrate before the hosts start, so neither the relay nor a worker meets a missing table.
        await MigrateAsync<IngressDbContext>(sqlConnectionString, IngressDbContext.Schema, options => new IngressDbContext(options));
        await MigrateAsync<SyncDbContext>(sqlConnectionString, SyncDbContext.Schema, options => new SyncDbContext(options));
        await MigrateAsync<MatchingDbContext>(sqlConnectionString, MatchingDbContext.Schema, options => new MatchingDbContext(options));

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

        _fakeMatchingPlatform = new WebApplicationFactory<FakeMatchingPlatform::Program>();
        FakeMatchingPlatform = new FakeMatchingPlatformTestClient(_fakeMatchingPlatform.CreateClient());

        _syncWorker = new WebApplicationFactory<SyncWorker::Program>().WithWebHostBuilder(host =>
        {
            ConfigureWorker(host, sqlConnectionString, eventHubsConnectionString);
            host.UseSetting("Crm:BaseUrl", $"http://{CrmHost}")
                .ConfigureTestServices(services => RouteHttpClients(services, new Dictionary<string, TestServer>
                {
                    [TeamtailorHost] = _fakeTeamtailor.Server,
                    [CrmHost] = _fakeCrm.Server,
                }));
        });

        _matchingWorker = new WebApplicationFactory<MatchingWorker::Program>().WithWebHostBuilder(host =>
        {
            ConfigureWorker(host, sqlConnectionString, eventHubsConnectionString);
            host.UseSetting("MatchingPlatform:BaseUrl", $"http://{MatchingPlatformHost}")
                .UseSetting("Llm:Provider", "Fake")
                .UseSetting("Llm:Fake:Mode", "Valid")
                // M3 waits for this timeout once; the production 15 s would only make it slower.
                .UseSetting("Llm:Timeout", "00:00:02")
                .ConfigureTestServices(services => RouteHttpClients(services, new Dictionary<string, TestServer>
                {
                    [TeamtailorHost] = _fakeTeamtailor.Server,
                    [MatchingPlatformHost] = _fakeMatchingPlatform.Server,
                }));
        });

        // A worker has no request to trigger its start: touching the server starts the host and its event processor.
        _ = _syncWorker.Server;
        _ = _matchingWorker.Server;

        SyncCheckpoints = CreateCheckpointReader(_syncWorker.Services);
        MatchingCheckpoints = CreateCheckpointReader(_matchingWorker.Services);

        Hub = new HubReader(eventHubsConnectionString, HubName);
    }

    public async Task<TResult> QueryIngressDbAsync<TResult>(Func<IngressDbContext, Task<TResult>> query)
    {
        await using var scope = _ingress!.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IngressDbContext>());
    }

    public Task<TResult> QuerySyncDbAsync<TResult>(Func<SyncDbContext, Task<TResult>> query) =>
        QueryWorkerDbAsync(_syncWorker!.Services, query);

    public Task<TResult> QueryMatchingDbAsync<TResult>(Func<MatchingDbContext, Task<TResult>> query) =>
        QueryWorkerDbAsync(_matchingWorker!.Services, query);

    /// <summary>
    /// A tag for the CV of a scenario's candidate, so <see cref="CountModelCalls"/> can attribute calls to it.
    /// The model never receives our candidate id: the tag is only in CV text a test wrote.
    /// </summary>
    public static string CvTag(string candidateId) => $"(ref {candidateId})";

    /// <summary>How many times Matching.Worker's <see cref="FakeChatClient"/> was called with the candidate's tagged CV.</summary>
    public int CountModelCalls(string candidateId) =>
        _matchingWorker!.Services.GetRequiredService<FakeChatClient>().ReceivedCvTexts
            .Count(cv => cv.Contains(CvTag(candidateId), StringComparison.Ordinal));

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
        foreach (var host in new IAsyncDisposable?[] { _matchingWorker, _syncWorker, _fakeMatchingPlatform, _fakeCrm, _fakeTeamtailor, _ingress })
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

    /// <summary>Settings both workers share: infrastructure, the Teamtailor API and the handler loop's test tunables.</summary>
    private void ConfigureWorker(IWebHostBuilder host, string sqlConnectionString, string eventHubsConnectionString) => host
        .UseSetting("ConnectionStrings:Sql", sqlConnectionString)
        .UseSetting("EventHubs:ConnectionString", eventHubsConnectionString)
        .UseSetting("Checkpoints:BlobConnectionString", _azurite.GetConnectionString())
        .UseSetting("Teamtailor:BaseUrl", $"http://{TeamtailorHost}")
        .UseSetting("Teamtailor:ApiKey", "fake-api-key")
        // Outages in scenarios last seconds; the production cap of 30 s would only make them slower.
        .UseSetting("Consumer:MaxBackoff", "00:00:05")
        // With 5 attempts, a 503 misclassified as unclassified would outlast S4's 30 s outage and still succeed;
        // with 2 it is parked during the outage, so S4 proves that transient failures wait (invariant 8).
        .UseSetting("Consumer:MaxUnclassifiedAttempts", "2");

    // The container and consumer group the worker itself uses, not a copy of their names.
    private static CheckpointReader CreateCheckpointReader(IServiceProvider worker) =>
        new(worker.GetRequiredService<BlobContainerClient>(), worker.GetRequiredService<IConfiguration>()["EventHubs:ConsumerGroup"]!);

    private static async Task<TResult> QueryWorkerDbAsync<TDbContext, TResult>(IServiceProvider worker, Func<TDbContext, Task<TResult>> query)
        where TDbContext : DbContext
    {
        await using var db = await worker.GetRequiredService<IDbContextFactory<TDbContext>>().CreateDbContextAsync();
        return await query(db);
    }

    private static async Task MigrateAsync<TDbContext>(string connectionString, string schema, Func<DbContextOptions<TDbContext>, TDbContext> create)
        where TDbContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;
        await using var db = create(options);
        await db.Database.MigrateAsync();
    }

    private static void RouteHttpClients(IServiceCollection services, IReadOnlyDictionary<string, TestServer> servers) =>
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => new HostRouter(servers)));
}

[CollectionDefinition(Name)]
public sealed class SystemCollection : ICollectionFixture<SystemFixture>
{
    public const string Name = "System";
}
