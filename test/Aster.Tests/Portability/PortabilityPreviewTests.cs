using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityPreviewTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceManager manager;
    private readonly IResourcePortabilityService portability;

    public PortabilityPreviewTests()
    {
        provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

        definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        manager = provider.GetRequiredService<IResourceManager>();
        portability = provider.GetRequiredService<IResourcePortabilityService>();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task PreviewImportAsync_ValidSnapshot_ReturnsPlannedCountsAndDoesNotMutateStore()
    {
        var snapshot = CreateSnapshot();

        var preview = await portability.PreviewImportAsync(snapshot);

        Assert.True(preview.CanImport);
        Assert.Empty(preview.Diagnostics);
        Assert.Equal(1, preview.Counts.Definitions);
        Assert.Equal(1, preview.Counts.Resources);
        Assert.Equal(1, preview.Counts.ResourceVersions);
        Assert.Equal(1, preview.Counts.ActivationEntries);
        Assert.Contains(
            preview.IdentityMap,
            static mapping =>
                mapping.EntityKind == PortableEntityKind.DefinitionVersion
                && mapping.SourceId == """["Product",1]"""
                && mapping.TargetId == """["Product",1]"""
                && mapping.Reason == PortableIdentityMappingReason.Preserved);

        Assert.Null(await definitionStore.GetDefinitionAsync("Product"));
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));
        Assert.Empty(await manager.GetActiveVersionsAsync("product-1", "Published"));
    }

    [Fact]
    public async Task PreviewImportAsync_UnresolvedReferences_ReturnsDiagnosticsWithoutMutation()
    {
        var snapshot = CreateSnapshot() with
        {
            Definitions = [],
            ActivationStates =
            [
                new ActivationState
                {
                    ResourceId = "product-1",
                    Channel = "Published",
                    ActiveVersions = [1, 2],
                    LastUpdated = new DateTime(2026, 1, 2, 3, 5, 5, DateTimeKind.Utc),
                },
            ],
        };

        var preview = await portability.PreviewImportAsync(snapshot);

        Assert.False(preview.CanImport);
        Assert.Empty(preview.IdentityMap);
        Assert.Contains(preview.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.MissingDefinitionReference);
        Assert.Contains(preview.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.MissingResourceReference);
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));
    }

    [Fact]
    public async Task PreviewImportAsync_DuplicateSnapshotIdentity_ReturnsDiagnosticBeforePlanningWrites()
    {
        var snapshot = CreateSnapshot();
        snapshot = snapshot with
        {
            Resources =
            [
                snapshot.Resources[0],
                snapshot.Resources[0] with { Id = "product-1-v1-copy" },
            ],
            ActivationStates = [],
        };

        var preview = await portability.PreviewImportAsync(snapshot);

        Assert.False(preview.CanImport);
        var diagnostic = Assert.Single(
            preview.Diagnostics,
            static diagnostic => diagnostic.Code == PortableDiagnosticCodes.DuplicateSnapshotIdentity);
        Assert.Equal("""resources/["product-1",1]""", diagnostic.Path);
        Assert.Equal(0, preview.Counts.ResourceVersions);
        Assert.Empty(preview.IdentityMap);
    }

    [Fact]
    public async Task PreviewImportAsync_RemapDivergentCollision_ProducesDeterministicIdentityMapWithoutMutation()
    {
        await portability.ImportAsync(CreateSnapshotWithResourceOwner("target-owner"));
        var snapshot = CreateSnapshot();
        var options = new PortableImportOptions { CollisionMode = PortableImportCollisionMode.RemapDivergent };

        var first = await portability.PreviewImportAsync(snapshot, options);
        var second = await portability.PreviewImportAsync(snapshot, options);

        Assert.True(first.CanImport);
        Assert.Equal(
            first.IdentityMap.Select(static mapping => (mapping.EntityKind, mapping.SourceId, mapping.TargetId, mapping.Reason)),
            second.IdentityMap.Select(static mapping => (mapping.EntityKind, mapping.SourceId, mapping.TargetId, mapping.Reason)));
        Assert.Equal(
            first.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Path, diagnostic.Message)),
            second.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Path, diagnostic.Message)));
        Assert.Contains(
            first.IdentityMap,
            static mapping =>
                mapping.EntityKind == PortableEntityKind.ResourceVersion
                && mapping.SourceId == """["product-1",1]"""
                && mapping.TargetId == """["product-1__imported",1]"""
                && mapping.Reason == PortableIdentityMappingReason.RemappedDivergent);
        Assert.Contains(
            first.IdentityMap,
            static mapping =>
                mapping.EntityKind == PortableEntityKind.ActivationEntry
                && mapping.SourceId == """["product-1","Published"]"""
                && mapping.TargetId == """["product-1__imported","Published"]"""
                && mapping.Reason == PortableIdentityMappingReason.RemappedDivergent);
        Assert.Null(await manager.GetLatestVersionAsync("product-1__imported"));
        Assert.Empty(await manager.GetActiveVersionsAsync("product-1__imported", "Published"));
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

    private static PortableSnapshot CreateSnapshotWithResourceOwner(string owner)
    {
        var snapshot = CreateSnapshot();

        return snapshot with
        {
            Resources =
            [
                snapshot.Resources[0] with { Owner = owner },
            ],
        };
    }
}
