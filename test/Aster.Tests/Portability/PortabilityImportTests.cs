using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityImportTests : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceManager manager;
    private readonly IResourcePortabilityService portability;

    public PortabilityImportTests()
    {
        provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

        definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        manager = provider.GetRequiredService<IResourceManager>();
        portability = provider.GetRequiredService<IResourcePortabilityService>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ImportAsync_EmptyStore_PreservesDefinitionsResourcesAndActivationState()
    {
        var snapshot = CreateSnapshot();

        var result = await portability.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Equal(1, result.Counts.Definitions);
        Assert.Equal(1, result.Counts.Resources);
        Assert.Equal(1, result.Counts.ResourceVersions);
        Assert.Equal(1, result.Counts.ActivationEntries);

        var exported = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["product-1"],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });

        Assert.NotNull(exported.Snapshot);
        Assert.Equal(snapshot.Definitions.Select(static definition => (definition.DefinitionId, definition.Id, definition.Version)), exported.Snapshot.Definitions.Select(static definition => (definition.DefinitionId, definition.Id, definition.Version)));
        Assert.Equal(snapshot.Resources.Select(static resource => (resource.ResourceId, resource.Id, resource.DefinitionId, resource.DefinitionVersion, resource.Version)), exported.Snapshot.Resources.Select(static resource => (resource.ResourceId, resource.Id, resource.DefinitionId, resource.DefinitionVersion, resource.Version)));
        Assert.Equal(snapshot.ActivationStates.Select(static state => (state.ResourceId, state.Channel, state.LastUpdated, Versions: state.ActiveVersions.ToArray())), exported.Snapshot.ActivationStates.Select(static state => (state.ResourceId, state.Channel, state.LastUpdated, Versions: state.ActiveVersions.ToArray())));
    }

    [Fact]
    public async Task ImportAsync_IdenticalContentAlreadyExists_ReturnsNoOp()
    {
        var snapshot = CreateSnapshot();
        await portability.ImportAsync(snapshot);

        var result = await portability.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.NoOp, result.Status);
        Assert.Equal(0, result.Counts.Definitions);
        Assert.Equal(0, result.Counts.ResourceVersions);
        Assert.Equal(0, result.Counts.ActivationEntries);
        Assert.Equal(3, result.Counts.ReusedIdenticalItems);
        Assert.All(result.IdentityMap, static mapping => Assert.Equal(PortableIdentityMappingReason.ReusedIdentical, mapping.Reason));
    }

    [Fact]
    public async Task ImportAsync_IdenticalActivationStateWithDifferentVersionOrder_ReturnsNoOp()
    {
        var snapshot = CreateSnapshotWithTwoActiveVersions([1, 2]);
        await portability.ImportAsync(snapshot);

        var result = await portability.ImportAsync(CreateSnapshotWithTwoActiveVersions([2, 1, 1]));

        Assert.Equal(PortableImportStatus.NoOp, result.Status);
        Assert.Equal(0, result.Counts.Definitions);
        Assert.Equal(0, result.Counts.ResourceVersions);
        Assert.Equal(0, result.Counts.ActivationEntries);
        Assert.Equal(4, result.Counts.ReusedIdenticalItems);
    }

    [Fact]
    public async Task ImportAsync_InMemoryStoreImportsVersionsOutOfOrder_PreservesLatestVersionInvariant()
    {
        await portability.ImportAsync(CreateVersionedSnapshot(definitionVersion: 2, resourceVersion: 2));
        await portability.ImportAsync(CreateVersionedSnapshot(definitionVersion: 1, resourceVersion: 1));

        var latestDefinition = await definitionStore.GetDefinitionAsync("Product");
        var latestResource = await manager.GetLatestVersionAsync("product-ordered");

        Assert.NotNull(latestDefinition);
        Assert.Equal(2, latestDefinition.Version);
        Assert.NotNull(latestResource);
        Assert.Equal(2, latestResource.Version);
    }

    [Fact]
    public async Task ImportAsync_StrictDivergentDefinitionCollision_FailsWithoutWritingResources()
    {
        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());
        var snapshot = CreateSnapshot();

        var result = await portability.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.DivergentIdentityCollision, diagnostic.Code);
        var mapping = Assert.Single(
            result.IdentityMap,
            static mapping => mapping.Reason == PortableIdentityMappingReason.CollidedDivergent);
        Assert.Equal(PortableEntityKind.DefinitionVersion, mapping.EntityKind);
        Assert.Equal("""["Product",1]""", mapping.SourceId);
        Assert.Equal("""["Product",1]""", mapping.TargetId);
        Assert.Equal(PortableIdentityMappingReason.CollidedDivergent, mapping.Reason);
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));
    }

    [Fact]
    public async Task ImportAsync_RemapDivergentDefinitionCollision_UsesDedicatedDeferredDiagnostic()
    {
        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());
        var snapshot = CreateSnapshot();

        var result = await portability.ImportAsync(
            snapshot,
            new PortableImportOptions { CollisionMode = PortableImportCollisionMode.RemapDivergent });

        Assert.Equal(PortableImportStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.RemapDivergentNotImplemented, diagnostic.Code);
    }

    [Fact]
    public async Task PreviewImportAsync_DuplicateVersionSpecificResourceIds_UsesExplicitIdPath()
    {
        var snapshot = CreateSnapshot() with
        {
            Resources =
            [
                new Resource
                {
                    ResourceId = "product-1",
                    Id = "duplicate-version-id",
                    DefinitionId = "Product",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                },
                new Resource
                {
                    ResourceId = "product-2",
                    Id = "duplicate-version-id",
                    DefinitionId = "Product",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc),
                },
            ],
            ActivationStates = [],
        };

        var result = await portability.PreviewImportAsync(snapshot);

        Assert.False(result.CanImport);
        var diagnostic = Assert.Single(result.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.DuplicateSnapshotIdentity);
        Assert.Equal("resources/id/duplicate-version-id", diagnostic.Path);
    }

    [Fact]
    public async Task ApplyImportAsync_InMemoryFailure_RollsBackPreviouslyAppliedItems()
    {
        var store = provider.GetRequiredService<IResourcePortabilityStore>();
        var snapshot = new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "RollbackProduct",
                    Id = "rollback-product-definition-v1",
                    Version = 1,
                },
            ],
            Resources =
            [
                new Resource
                {
                    ResourceId = "rollback-product-1",
                    Id = "rollback-product-1-v1",
                    DefinitionId = "RollbackProduct",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                },
                new Resource
                {
                    ResourceId = "rollback-product-1",
                    Id = "rollback-product-1-v1-duplicate",
                    DefinitionId = "RollbackProduct",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc),
                },
            ],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyImportAsync(snapshot).AsTask());

        Assert.Null(await definitionStore.GetDefinitionAsync("RollbackProduct"));
        Assert.Null(await manager.GetLatestVersionAsync("rollback-product-1"));
    }

    [Fact]
    public async Task RegisterDefinitionAsync_AfterHighVersionImport_UsesNextHighestVersion()
    {
        var store = provider.GetRequiredService<IResourcePortabilityStore>();
        await store.ApplyImportAsync(new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "VersionedProduct",
                    Id = "versioned-product-definition-v10",
                    Version = 10,
                },
            ],
        });

        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("VersionedProduct")
            .Build());

        var latest = await definitionStore.GetDefinitionAsync("VersionedProduct");

        Assert.NotNull(latest);
        Assert.Equal(11, latest.Version);
    }

    [Fact]
    public async Task ImportAsync_ApplyFailure_ReturnsFailedResultWithDiagnostic()
    {
        var service = new ResourcePortabilityService(new ThrowingApplyPortabilityStore());

        var result = await service.ImportAsync(CreateSnapshot());

        Assert.Equal(PortableImportStatus.Failed, result.Status);
        Assert.Equal(0, result.Counts.Definitions);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.ImportApplyFailed, diagnostic.Code);
        Assert.Contains("simulated apply race", diagnostic.Message, StringComparison.Ordinal);
    }

    private static PortableSnapshot CreateSnapshot()
    {
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        return new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "Product",
                    Id = "product-definition-v1",
                    Version = 1,
                },
            ],
            Resources =
            [
                new Resource
                {
                    ResourceId = "product-1",
                    Id = "product-1-v1",
                    DefinitionId = "Product",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = created,
                },
            ],
            ActivationStates =
            [
                new ActivationState
                {
                    ResourceId = "product-1",
                    Channel = "Published",
                    ActiveVersions = [1],
                    LastUpdated = created.AddMinutes(1),
                },
            ],
        };
    }

    private static PortableSnapshot CreateSnapshotWithTwoActiveVersions(IReadOnlyList<int> activeVersions)
    {
        var snapshot = CreateSnapshot();
        var v2Created = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc);

        return snapshot with
        {
            Resources =
            [
                snapshot.Resources[0],
                new Resource
                {
                    ResourceId = "product-1",
                    Id = "product-1-v2",
                    DefinitionId = "Product",
                    DefinitionVersion = 1,
                    Version = 2,
                    Created = v2Created,
                },
            ],
            ActivationStates =
            [
                snapshot.ActivationStates[0] with
                {
                    ActiveVersions = activeVersions,
                },
            ],
        };
    }

    private static PortableSnapshot CreateVersionedSnapshot(int definitionVersion, int resourceVersion)
    {
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddDays(resourceVersion);

        return new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "Product",
                    Id = $"product-definition-v{definitionVersion}",
                    Version = definitionVersion,
                },
            ],
            Resources =
            [
                new Resource
                {
                    ResourceId = "product-ordered",
                    Id = $"product-ordered-v{resourceVersion}",
                    DefinitionId = "Product",
                    DefinitionVersion = definitionVersion,
                    Version = resourceVersion,
                    Created = created,
                },
            ],
        };
    }

    private sealed class ThrowingApplyPortabilityStore : IResourcePortabilityStore
    {
        public ValueTask<PortableStoreSnapshot> ReadSnapshotAsync(
            PortableStoreReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PortableStoreSnapshot());

        public ValueTask<PortableTargetState> ReadTargetStateAsync(
            PortableSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PortableTargetState());

        public ValueTask ApplyImportAsync(
            PortableSnapshot plannedSnapshot,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated apply race");
    }
}
