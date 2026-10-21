using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Persistence.PostgresJsonb;
using Aster.Persistence.SqlServerJson;
using Aster.Persistence.SqliteJson;
using Aster.Tests.Persistence;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Definitions;

/// <summary>
/// #175: <see cref="IAspectDefinitionStore.RegisterIfLatestAsync"/> appends only when the caller's expected latest
/// version is still current, atomically on every provider, so "create only if missing" and "save only if still on
/// version N" are safe across processes.
/// </summary>
public sealed class AspectRegistrationConcurrencyTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string Postgres = "postgres";
    private const string SqlServer = "sqlserver";
    private const int Writers = 8;

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");
    private readonly PostgresTestDatabase postgres = new();

    private readonly SqlServerTestDatabase sqlServer = new();

    public void Dispose()
    {
        SqliteJsonTestDatabase.DeleteFiles(databasePath);
        postgres.Dispose();
        sqlServer.Dispose();
    }

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        else if (providerKind == Postgres)
            services.AddAsterPostgresJsonb(options => options.ConnectionString = postgres.ConnectionString);
        else if (providerKind == SqlServer)
            services.AddAsterSqlServerJson(options => options.ConnectionString = sqlServer.ConnectionString);

        return services.BuildServiceProvider();
    }

    private static AspectDefinition Seo(string facetId = "Slug") => new()
    {
        AspectDefinitionId = "Seo",
        Id = Guid.NewGuid().ToString("N"),
        Version = 0,
        FacetDefinitions = [new FacetDefinition { FacetDefinitionId = facetId, Id = Guid.NewGuid().ToString("N"), Version = 1, Type = "string" }],
    };

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    [InlineData(SqlServer)]
    public async Task RegistersWhenTheExpectedVersionIsLatest(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();

        var created = await store.RegisterIfLatestAsync(Seo(), 0);
        var edited = await store.RegisterIfLatestAsync(Seo("Title"), 1);

        Assert.Equal((AspectDefinitionRegistrationStatus.Registered, 1, 1), (created.Status, created.LatestVersion, created.Aspect!.Version));
        Assert.Equal((AspectDefinitionRegistrationStatus.Registered, 2, 2), (edited.Status, edited.LatestVersion, edited.Aspect!.Version));
        Assert.Equal("Title", Assert.Single((await store.GetLatestAsync("Seo"))!.FacetDefinitions).FacetDefinitionId);
    }

    [SkippableTheory]
    [InlineData(InMemory, 0)]
    [InlineData(InMemory, 1)]
    [InlineData(InMemory, 3)]
    [InlineData(Sqlite, 0)]
    [InlineData(Sqlite, 3)]
    [InlineData(Postgres, 0)]
    [InlineData(SqlServer, 0)]
    [InlineData(Postgres, 1)]
    [InlineData(SqlServer, 1)]
    public async Task StaleExpectedVersion_ConflictsAndWritesNothing(string providerKind, int expectedLatestVersion)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();
        await store.RegisterAsync(Seo());
        await store.RegisterAsync(Seo("Title"));

        var result = await store.RegisterIfLatestAsync(Seo("Body"), expectedLatestVersion);

        Assert.Equal((AspectDefinitionRegistrationStatus.VersionConflict, 2), (result.Status, result.LatestVersion));
        Assert.False(result.IsRegistered);
        Assert.Null(result.Aspect);
        Assert.Equal([1, 2], (await store.ListVersionsAsync("Seo")).Select(static aspect => aspect.Version));
    }

    [Fact]
    public async Task NegativeExpectedVersion_Throws()
    {
        await using var provider = CreateProvider(InMemory);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await provider.GetRequiredService<IAspectDefinitionStore>().RegisterIfLatestAsync(Seo(), -1));
    }

    [SkippableTheory]
    [InlineData(InMemory, 0)]
    [InlineData(InMemory, 1)]
    [InlineData(Sqlite, 0)]
    [InlineData(Sqlite, 1)]
    [InlineData(Postgres, 0)]
    [InlineData(SqlServer, 0)]
    [InlineData(Postgres, 1)]
    [InlineData(SqlServer, 1)]
    public async Task ConcurrentRegistrations_OnTheSameExpectedVersion_ExactlyOneSucceeds(string providerKind, int expectedLatestVersion)
    {
        // Database providers each get their own service provider, as separate processes would; in-memory shares one.
        var shared = providerKind == InMemory ? CreateProvider(providerKind) : null;
        var providers = Enumerable.Range(0, Writers).Select(_ => shared ?? CreateProvider(providerKind)).Distinct().ToList();
        try
        {
            for (var version = 0; version < expectedLatestVersion; version++)
                await providers[0].GetRequiredService<IAspectDefinitionStore>().RegisterAsync(Seo());

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = Enumerable.Range(0, Writers).Select(index => Task.Run(async () =>
            {
                await start.Task;
                return await providers[index % providers.Count].GetRequiredService<IAspectDefinitionStore>()
                    .RegisterIfLatestAsync(Seo($"Facet{index}"), expectedLatestVersion);
            })).ToList();
            start.SetResult();
            var results = await Task.WhenAll(running);

            var winner = Assert.Single(results, static result => result.IsRegistered);
            Assert.All(results.Where(static result => !result.IsRegistered), result => Assert.Equal(expectedLatestVersion + 1, result.LatestVersion));
            var versions = await providers[0].GetRequiredService<IAspectDefinitionStore>().ListVersionsAsync("Seo");
            Assert.Equal(expectedLatestVersion + 1, versions.Count);
            Assert.Equal(winner.Aspect!.Id, versions[^1].Id);
        }
        finally
        {
            foreach (var provider in providers)
                await provider.DisposeAsync();
        }
    }
}
