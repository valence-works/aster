using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonStartupConcurrencyTests : IDisposable
{
    private const int StartupAttemptCount = 8;

    private readonly List<string> databasePaths = [];

    public void Dispose()
    {
        foreach (var databasePath in databasePaths)
            SqliteJsonTestDatabase.DeleteFiles(databasePath);
    }

    [Fact]
    public async Task ConcurrentFreshDatabaseInitialization_CreatesUsableSchema()
    {
        var databasePath = NewDatabasePath("aster-startup-fresh");

        await RunConcurrentlyAsync(() =>
        {
            using var provider = CreateProvider(databasePath);
            _ = provider.GetRequiredService<IResourceVersionReader>();
            _ = provider.GetRequiredService<IResourceQueryService>();
        });

        await using var verifier = CreateProvider(databasePath);
        var definitions = verifier.GetRequiredService<IResourceDefinitionStore>();
        var manager = verifier.GetRequiredService<IResourceManager>();

        await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["Title"] = new { Title = "Fresh Startup" },
            },
        });

        var reader = verifier.GetRequiredService<IResourceVersionReader>();
        var persisted = await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = [resource.ResourceId],
        });

        var saved = Assert.Single(persisted);
        Assert.Equal("Product", saved.DefinitionId);
        Assert.Equal(1, SqliteJsonTestDatabase.CountRows(databasePath, "resource_definitions"));
        Assert.Equal(1, SqliteJsonTestDatabase.CountRows(databasePath, "resource_versions"));
        AssertTenantAwarePrimaryKeys(databasePath);
    }

    [Fact]
    public async Task ConcurrentExistingDatabaseInitialization_PreservesPersistedStateAndShape()
    {
        var databasePath = NewDatabasePath("aster-startup-existing");
        var tenant = TenantScope.FromTenantId("tenant-a");
        const string resourceId = "tenant-product";

        await using (var seed = CreateProvider(databasePath))
        {
            await PolicyTestFixtures.RegisterProductDefinitionAsync(seed, tenant);
            await PolicyTestFixtures.SaveResourceAsync(seed, resourceId, tenantScope: tenant);
            await PolicyTestFixtures.ActivateAsync(seed, resourceId, version: 1, tenant);
            await seed.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
            {
                TenantScope = tenant,
                ResourceId = resourceId,
                State = ResourceLifecycleMarkerState.Archived,
                MarkedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                Reason = "startup-concurrency-test",
            });
        }

        await RunConcurrentlyAsync(() =>
        {
            using var provider = CreateProvider(databasePath);
            _ = provider.GetRequiredService<IResourceVersionReader>();
            _ = provider.GetRequiredService<IResourceQueryService>();
        });

        await using var verifier = CreateProvider(databasePath);
        var definition = await verifier
            .GetRequiredService<IResourceDefinitionStore>()
            .GetDefinitionAsync("Product", tenant, CancellationToken.None);
        var versions = await PolicyTestFixtures.ReadVersionsAsync(verifier, resourceId, tenant);
        var activationStates = await verifier
            .GetRequiredService<IResourceActivationStateReader>()
            .ReadActivationStatesAsync([resourceId], tenant);
        var marker = await verifier
            .GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync(resourceId, tenant);

        Assert.NotNull(definition);
        var version = Assert.Single(versions);
        Assert.Equal(resourceId, version.ResourceId);
        var activation = Assert.Single(activationStates);
        Assert.Equal("Published", activation.Channel);
        Assert.Equal([1], activation.ActiveVersions);
        Assert.NotNull(marker);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, marker.State);

        Assert.Equal(1, SqliteJsonTestDatabase.CountRows(databasePath, "resource_definitions"));
        Assert.Equal(1, SqliteJsonTestDatabase.CountRows(databasePath, "resource_versions"));
        Assert.Equal(1, SqliteJsonTestDatabase.CountRows(databasePath, "activation_states"));
        Assert.Equal(1, SqliteJsonTestDatabase.CountRows(databasePath, "lifecycle_markers"));
        Assert.Equal(1, SqliteJsonTestDatabase.CountRows(
            databasePath,
            "resource_versions",
            "tenant_id = 'tenant-a' AND resource_id = 'tenant-product' AND version = 1"));
        Assert.DoesNotContain(
            SqliteJsonTestDatabase.ReadTableNames(databasePath),
            static name => name.Contains("__legacy_tenant_bootstrap", StringComparison.Ordinal));
        AssertTenantAwarePrimaryKeys(databasePath);
    }

    [Fact]
    public async Task ConcurrentInitializeSchemaFalseConstruction_DoesNotCreateDatabase()
    {
        var databasePath = NewDatabasePath("aster-startup-no-schema");

        await RunConcurrentlyAsync(() =>
        {
            using var provider = CreateProvider(databasePath, initializeSchema: false);

            Assert.Equal(
                SqliteJsonQueryCapabilitiesProvider.ProviderKey,
                provider.GetRequiredService<IResourceQueryProviderIdentity>().ProviderKey);
            Assert.Equal(
                SqliteJsonQueryCapabilitiesProvider.ProviderKey,
                provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities.ProviderKey);
            _ = provider.GetRequiredService<IResourceQueryService>();
            _ = provider.GetRequiredService<IResourceVersionReader>();
        });

        Assert.False(File.Exists(databasePath));
    }

    private string NewDatabasePath(string prefix)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
        databasePaths.Add(databasePath);
        return databasePath;
    }

    private static async Task RunConcurrentlyAsync(Action action)
    {
        using var barrier = new Barrier(StartupAttemptCount);
        var tasks = Enumerable.Range(0, StartupAttemptCount)
            .Select(_ => Task.Run(() =>
            {
                barrier.SignalAndWait();
                action();
            }))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private static ServiceProvider CreateProvider(string databasePath, bool initializeSchema = true) =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = $"Data Source={databasePath}";
                options.InitializeSchema = initializeSchema;
            })
            .BuildServiceProvider();

    private static void AssertTenantAwarePrimaryKeys(string databasePath)
    {
        SqliteJsonTestDatabase.AssertPrimaryKey(databasePath, "resource_definitions", ["tenant_id", "definition_id", "version"]);
        SqliteJsonTestDatabase.AssertPrimaryKey(databasePath, "resource_versions", ["tenant_id", "resource_id", "version"]);
        SqliteJsonTestDatabase.AssertPrimaryKey(databasePath, "activation_states", ["tenant_id", "resource_id", "channel"]);
        SqliteJsonTestDatabase.AssertPrimaryKey(databasePath, "lifecycle_markers", ["tenant_id", "resource_id"]);
    }
}
