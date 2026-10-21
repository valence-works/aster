using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Definitions;

/// <summary>
/// #158: <see cref="IResourceDefinitionStore.RegisterDefinitionIfLatestAsync"/> appends only when the caller's
/// expected latest version is still current, atomically on both providers, and the aspect upgrade service uses
/// it so a concurrent definition edit is never superseded by a copy of the version it read.
/// </summary>
public sealed class DefinitionRegistrationConcurrencyTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static ResourceDefinition Article(string facetId = "Title") =>
        new ResourceDefinitionBuilder().WithDefinitionId("Article").WithAspect("Content").WithFacet(facetId, "string").Build();

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RegistersWhenTheExpectedVersionIsLatest(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IResourceDefinitionStore>();

        var created = await store.RegisterDefinitionIfLatestAsync(Article(), 0, TenantScope.Default, CancellationToken.None);
        var edited = await store.RegisterDefinitionIfLatestAsync(Article("Summary"), 1, TenantScope.Default, CancellationToken.None);

        Assert.Equal((ResourceDefinitionRegistrationStatus.Registered, 1, 1), (created.Status, created.LatestVersion, created.Definition!.Version));
        Assert.Equal((ResourceDefinitionRegistrationStatus.Registered, 2, 2), (edited.Status, edited.LatestVersion, edited.Definition!.Version));
        Assert.True(edited.IsRegistered);
        Assert.Equal(edited.Definition.Id, (await store.GetDefinitionAsync("Article"))!.Id);
    }

    [Theory]
    [InlineData(InMemory, 0)]
    [InlineData(InMemory, 1)]
    [InlineData(InMemory, 3)]
    [InlineData(Sqlite, 0)]
    [InlineData(Sqlite, 1)]
    [InlineData(Sqlite, 3)]
    public async Task StaleExpectedVersion_ConflictsAndWritesNothing(string providerKind, int expectedLatestVersion)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        await store.RegisterDefinitionAsync(Article());
        await store.RegisterDefinitionAsync(Article("Summary"));
        var latest = await store.GetDefinitionAsync("Article");

        var result = await store.RegisterDefinitionIfLatestAsync(Article("Body"), expectedLatestVersion, TenantScope.Default, CancellationToken.None);

        Assert.Equal((ResourceDefinitionRegistrationStatus.VersionConflict, 2), (result.Status, result.LatestVersion));
        Assert.False(result.IsRegistered);
        Assert.Null(result.Definition);
        Assert.Equal((2, latest!.Id), ((await store.GetDefinitionAsync("Article"))!.Version, (await store.GetDefinitionAsync("Article"))!.Id));
        Assert.Null(await store.GetDefinitionVersionAsync("Article", 3));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ExpectedVersions_AreTenantScoped(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        await store.RegisterDefinitionAsync(Article());

        var inOtherTenant = await store.RegisterDefinitionIfLatestAsync(Article(), 0, TenantScope.FromTenantId("other"), CancellationToken.None);

        Assert.Equal((ResourceDefinitionRegistrationStatus.Registered, 1), (inOtherTenant.Status, inOtherTenant.LatestVersion));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task NegativeExpectedVersion_Throws(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IResourceDefinitionStore>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.RegisterDefinitionIfLatestAsync(Article(), -1, TenantScope.Default, CancellationToken.None));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ConcurrentEditsOfTheSameVersion_ExactlyOneWins(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        await store.RegisterDefinitionAsync(Article());

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
            await store.RegisterDefinitionIfLatestAsync(Article($"Facet{index}"), 1, TenantScope.Default, CancellationToken.None))));

        var winner = Assert.Single(results, static result => result.IsRegistered);
        Assert.Equal(2, winner.LatestVersion);
        Assert.All(results.Where(static result => !result.IsRegistered), static result => Assert.Equal(2, result.LatestVersion));
        Assert.Equal(winner.Definition!.Id, (await store.GetDefinitionAsync("Article"))!.Id);
        Assert.Null(await store.GetDefinitionVersionAsync("Article", 3));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task AspectUpgrade_ReportsAConflictInsteadOfSupersedingAConcurrentEdit(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await aspects.RegisterAsync(new AspectDefinition
        {
            AspectDefinitionId = "Seo",
            Id = Guid.NewGuid().ToString(),
            Version = 0,
            FacetDefinitions = [new FacetDefinition { FacetDefinitionId = "Slug", Id = "slug", Version = 1, Type = "string" }],
        });
        await definitions.RegisterDefinitionAsync(await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects));
        await aspects.RegisterAsync((await aspects.GetLatestAsync("Seo"))! with { Id = Guid.NewGuid().ToString(), RequiresName = false });

        // Another writer registers Article v2 right after the upgrade reads v1.
        var racing = new ConcurrentWriterDefinitionStore(definitions);
        var upgrades = new AspectUpgradeService(aspects, racing);

        var result = await upgrades.ApplyAsync(new AspectUpgradeRequest { AspectDefinitionId = "Seo", DefinitionIds = ["Article"] });

        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal((AspectUpgradeStatus.Failed, (int?)null), (outcome.Status, outcome.NewDefinitionVersion));
        Assert.Equal(SharedAspectDiagnosticCodes.DefinitionVersionConflict, Assert.Single(outcome.Diagnostics).Code);
        var latest = await definitions.GetDefinitionAsync("Article");
        Assert.Equal((2, racing.ConcurrentVersionId), (latest!.Version, latest.Id));

        // A retry reads v2 and upgrades it, reporting the version it wrote.
        var retry = await new AspectUpgradeService(aspects, definitions).ApplyAsync(new AspectUpgradeRequest { AspectDefinitionId = "Seo", DefinitionIds = ["Article"] });
        Assert.Equal((AspectUpgradeStatus.Upgraded, (int?)3), (retry.Outcomes[0].Status, retry.Outcomes[0].NewDefinitionVersion));
    }

    /// <summary>Registers one concurrent copy of a definition right after the first latest-version read.</summary>
    private sealed class ConcurrentWriterDefinitionStore(IResourceDefinitionStore inner) : IResourceDefinitionStore
    {
        public string ConcurrentVersionId { get; } = Guid.NewGuid().ToString();

        private bool raced;

        public async ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, TenantScope tenantScope, CancellationToken cancellationToken)
        {
            var latest = await inner.GetDefinitionAsync(definitionId, tenantScope, cancellationToken);
            if (latest is not null && !raced)
            {
                raced = true;
                await inner.RegisterDefinitionAsync(latest with { Id = ConcurrentVersionId }, tenantScope, cancellationToken);
            }

            return latest;
        }

        public ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            GetDefinitionAsync(definitionId, TenantScope.Default, cancellationToken);

        public ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default) =>
            inner.GetDefinitionVersionAsync(definitionId, version, cancellationToken);

        public ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, TenantScope tenantScope, CancellationToken cancellationToken) =>
            inner.GetDefinitionVersionAsync(definitionId, version, tenantScope, cancellationToken);

        public ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default) =>
            inner.RegisterDefinitionAsync(definition, cancellationToken);

        public ValueTask RegisterDefinitionAsync(ResourceDefinition definition, TenantScope tenantScope, CancellationToken cancellationToken) =>
            inner.RegisterDefinitionAsync(definition, tenantScope, cancellationToken);

        public ValueTask<ResourceDefinitionRegistrationResult> RegisterDefinitionIfLatestAsync(
            ResourceDefinition definition,
            int expectedLatestVersion,
            TenantScope tenantScope,
            CancellationToken cancellationToken) =>
            inner.RegisterDefinitionIfLatestAsync(definition, expectedLatestVersion, tenantScope, cancellationToken);

        public ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
            inner.ListDefinitionsAsync(cancellationToken);

        public ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(TenantScope tenantScope, CancellationToken cancellationToken) =>
            inner.ListDefinitionsAsync(tenantScope, cancellationToken);
    }
}
