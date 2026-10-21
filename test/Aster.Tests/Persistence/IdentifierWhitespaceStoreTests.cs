using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

/// <summary>
/// Identifiers with leading or trailing whitespace (#230): every write that creates a record rejects them with the same
/// code on every provider, and records stored before the rule existed stay readable, updatable and removable.
/// </summary>
public abstract class IdentifierWhitespaceStoreTests(IAsterTestDatabase database) : IDisposable
{
    private static readonly TenantScope PaddedTenant = new() { TenantId = " legacy-tenant" };

    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableTheory]
    [InlineData(" product-1")]
    [InlineData("product-1 ")]
    [InlineData("\tproduct-1")]
    [InlineData("product-1\n")]
    [InlineData(" product-1")]
    public async Task CreateAsync_RejectsResourceIdWithSurroundingWhitespace_AndWritesNothing(string resourceId)
    {
        await using var provider = Database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);

        var exception = await Assert.ThrowsAsync<InvalidIdentifierException>(
            () => TenantScopeTestFixtures.CreateProductAsync(provider, resourceId, "Padded"));

        Assert.Equal(InvalidIdentifierException.SurroundingWhitespaceCode, exception.Code);
        Assert.Equal(resourceId, exception.Identifier);
        Assert.Equal("resourceId", exception.ParamName);
        Assert.Empty(await ReadLatestAsync(provider));
    }

    [SkippableFact]
    public async Task CreateAsync_RejectsDefinitionIdWithSurroundingWhitespace()
    {
        await using var provider = Database.CreateProvider();
        var manager = provider.GetRequiredService<IResourceManager>();

        var exception = await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await manager.CreateAsync("Product ", new CreateResourceRequest { ResourceId = "product-1" }));

        Assert.Equal("definitionId", exception.ParamName);
        Assert.Empty(await ReadLatestAsync(provider));
    }

    [SkippableFact]
    public async Task CreateAsync_RejectsTenantIdWithSurroundingWhitespace()
    {
        await using var provider = Database.CreateProvider();
        var manager = provider.GetRequiredService<IResourceManager>();

        var exception = await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { ResourceId = "product-1", TenantScope = PaddedTenant }));

        Assert.Equal(PaddedTenant.TenantId, exception.Identifier);
        Assert.Empty(await ReadLatestAsync(provider, PaddedTenant));
    }

    [SkippableFact]
    public async Task CreateAsync_BlankResourceId_StillGeneratesAnId()
    {
        await using var provider = Database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);

        var created = await TenantScopeTestFixtures.CreateProductAsync(provider, "   ", "Generated");

        Assert.False(string.IsNullOrWhiteSpace(created.ResourceId));
        Assert.Equal(created.ResourceId.Trim(), created.ResourceId);
    }

    [SkippableFact]
    public async Task CreateAsync_AcceptsInnerWhitespace()
    {
        await using var provider = Database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);

        var created = await TenantScopeTestFixtures.CreateProductAsync(provider, "product one", "Inner space");

        Assert.Equal("product one", created.ResourceId);
    }

    [SkippableFact]
    public async Task RegisterDefinition_RejectsDefinitionIdWithSurroundingWhitespace_OnEveryRegistrationPath()
    {
        await using var provider = Database.CreateProvider();
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        var padded = new ResourceDefinitionBuilder().WithDefinitionId("Product").Build() with { DefinitionId = "Product " };

        var register = await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await store.RegisterDefinitionAsync(padded, TenantScope.Default, CancellationToken.None));
        var registerIfLatest = await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await store.RegisterDefinitionIfLatestAsync(padded, 0, TenantScope.Default, CancellationToken.None));
        var paddedTenant = await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await store.RegisterDefinitionAsync(padded with { DefinitionId = "Product" }, PaddedTenant, CancellationToken.None));

        Assert.Equal(InvalidIdentifierException.SurroundingWhitespaceCode, register.Code);
        Assert.Equal("Product ", registerIfLatest.Identifier);
        Assert.Equal(PaddedTenant.TenantId, paddedTenant.Identifier);
        Assert.Empty(await store.ListDefinitionsAsync(TenantScope.Default, CancellationToken.None));
        Assert.Empty(await store.ListDefinitionsAsync(PaddedTenant, CancellationToken.None));
    }

    [SkippableFact]
    public async Task RegisterAspect_RejectsAspectDefinitionIdWithSurroundingWhitespace_OnEveryRegistrationPath()
    {
        await using var provider = Database.CreateProvider();
        var store = provider.GetRequiredService<IAspectDefinitionStore>();
        var padded = new AspectDefinition { AspectDefinitionId = " Seo", Id = "seo-1", Version = 1 };

        var register = await Assert.ThrowsAsync<InvalidIdentifierException>(async () => await store.RegisterAsync(padded));
        var registerIfLatest = await Assert.ThrowsAsync<InvalidIdentifierException>(async () => await store.RegisterIfLatestAsync(padded, 0));
        var paddedTenant = await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await store.RegisterAsync(padded with { AspectDefinitionId = "Seo" }, PaddedTenant));

        Assert.Equal(" Seo", register.Identifier);
        Assert.Equal(" Seo", registerIfLatest.Identifier);
        Assert.Equal(PaddedTenant.TenantId, paddedTenant.Identifier);
        Assert.Empty(await store.ListAsync());
        Assert.Empty(await store.ListAsync(PaddedTenant));
    }

    [SkippableFact]
    public async Task ActivateAsync_RejectsChannelWithSurroundingWhitespace_AndActivatesNothing()
    {
        await using var provider = Database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product-1", "Title");
        var manager = provider.GetRequiredService<IResourceManager>();

        var exception = await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await manager.ActivateAsync("product-1", 1, "Published "));

        Assert.Equal("channel", exception.ParamName);
        Assert.Empty(await manager.GetActiveVersionsAsync("product-1", "Published "));
        Assert.Empty(await manager.GetActiveVersionsAsync("product-1", "Published"));
    }

    [SkippableFact]
    public async Task ExistingPaddedIdentifiers_StayReadableUpdatableAndRemovable()
    {
        await using var provider = Database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await SeedLegacyResourceAsync(provider, TenantScope.Default);
        var manager = provider.GetRequiredService<IResourceManager>();

        Assert.NotNull(await manager.GetLatestVersionAsync("legacy "));
        Assert.NotNull(await manager.GetVersionAsync("legacy ", 1));
        Assert.Single(await manager.GetActiveVersionsAsync("legacy ", "Published "));

        // Padded and trimmed IDs stay distinct records; nothing trims on read.
        Assert.Null(await manager.GetLatestVersionAsync("legacy"));
        Assert.Empty(await manager.GetActiveVersionsAsync("legacy ", "Published"));

        var updated = await manager.UpdateAsync("legacy ", new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = { ["Title"] = new TenantScopeTestFixtures.TitleAspect("Updated") },
        });
        Assert.Equal("legacy ", updated.ResourceId);
        Assert.Equal(2, updated.Version);
        Assert.Equal([1, 2], (await manager.GetVersionsAsync("legacy ")).Select(static resource => resource.Version));

        var history = await provider.GetRequiredService<IResourceVersionHistoryService>()
            .GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = "legacy " });
        Assert.Equal("legacy ", history.ResourceId);
        Assert.Equal(2, history.Versions.Count);

        await manager.DeactivateAsync("legacy ", 1, "Published ");
        Assert.Empty(await manager.GetActiveVersionsAsync("legacy ", "Published "));

        var marker = await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "legacy ",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
        Assert.Empty(marker.Diagnostics);
        Assert.Equal("legacy ", marker.Marker!.ResourceId);
    }

    [SkippableFact]
    public async Task ExistingPaddedTenant_StaysReadable_ButAcceptsNoNewResources()
    {
        await using var provider = Database.CreateProvider();
        await SeedLegacyResourceAsync(provider, PaddedTenant);
        var manager = provider.GetRequiredService<IResourceManager>();

        var legacy = await manager.GetLatestVersionAsync("legacy ", PaddedTenant, CancellationToken.None);
        Assert.Equal(PaddedTenant.TenantId, legacy!.TenantScope.TenantId);

        await Assert.ThrowsAsync<InvalidIdentifierException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { ResourceId = "product-2", TenantScope = PaddedTenant }));
    }

    [SkippableFact]
    public async Task Import_ReportsPaddedSnapshotIdentifiersAsDiagnostics_AndWritesNothing()
    {
        await using var provider = Database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await SeedLegacyResourceAsync(provider, TenantScope.Default);
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        // Export reads existing data, so a padded ID exports; importing it would create new records and is refused.
        var export = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["legacy "],
        });
        var snapshot = Assert.IsType<PortableSnapshot>(export.Snapshot);

        var validation = await portability.ValidateAsync(snapshot);
        var target = TenantScope.FromTenantId("target");
        var import = await portability.ImportAsync(snapshot, new PortableImportOptions { TargetTenantScope = target });

        Assert.False(validation.IsValid);
        var paths = validation.Diagnostics
            .Where(static diagnostic => diagnostic.Code == PortableDiagnosticCodes.IdentifierWhitespace)
            .Select(static diagnostic => diagnostic.Path)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["activationStates/0/channel", "activationStates/0/resourceId", "resources/0/resourceId"], paths);
        Assert.All(
            validation.Diagnostics.Where(static diagnostic => diagnostic.Code == PortableDiagnosticCodes.IdentifierWhitespace),
            static diagnostic => Assert.Equal(PortableDiagnosticSeverity.Error, diagnostic.Severity));

        Assert.Equal(PortableImportStatus.Failed, import.Status);
        Assert.Contains(import.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.IdentifierWhitespace);
        Assert.Empty(await ReadLatestAsync(provider, target));
    }

    [SkippableFact]
    public async Task Import_ReportsPaddedTargetTenant_AndWritesNothing()
    {
        await using var provider = Database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product-1", "Title");
        var portability = provider.GetRequiredService<IResourcePortabilityService>();
        var export = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["product-1"],
        });

        var import = await portability.ImportAsync(export.Snapshot!, new PortableImportOptions { TargetTenantScope = PaddedTenant });

        Assert.Equal(PortableImportStatus.Failed, import.Status);
        var diagnostic = Assert.Single(import.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.IdentifierWhitespace);
        Assert.Equal("targetTenantScope/tenantId", diagnostic.Path);
        Assert.Empty(await ReadLatestAsync(provider, PaddedTenant));
    }

    // Writes through the provider's version writer, below the core create/activate rules, the way data stored before
    // the rule existed looks.
    private static async Task SeedLegacyResourceAsync(IServiceProvider provider, TenantScope tenant)
    {
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("legacy ", "Legacy", tenant));
        await writer.UpdateActivationAsync("legacy ", "Published ", new ActivationState
        {
            TenantScope = tenant,
            ResourceId = "legacy ",
            Channel = "Published ",
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });
    }

    private static async Task<List<Resource>> ReadLatestAsync(IServiceProvider provider, TenantScope? tenant = null) =>
        (await provider.GetRequiredService<IResourceVersionReader>().ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant ?? TenantScope.Default,
        })).ToList();
}

public sealed class InMemoryIdentifierWhitespaceStoreTests() : IdentifierWhitespaceStoreTests(new InMemoryCoreTestDatabase());

public sealed class SqliteJsonIdentifierWhitespaceStoreTests() : IdentifierWhitespaceStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbIdentifierWhitespaceStoreTests() : IdentifierWhitespaceStoreTests(new PostgresTestDatabase());

/// <summary>
/// The in-memory provider. Unlike the persistent databases, each provider it creates starts empty.
/// </summary>
file sealed class InMemoryCoreTestDatabase : IAsterTestDatabase
{
    public ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddAsterCore();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
    }
}
