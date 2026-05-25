using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyCompatibilityTests
{
    [Fact]
    public async Task ExistingResourceWrites_DoNotApplyPolicyMarkersAutomatically()
    {
        await using var provider = PolicyTestFixtures.CreateCoreProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);

        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var marker = await provider.GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync("product-1", Aster.Core.Models.Tenancy.TenantScope.Default);
        Assert.Null(marker);
    }

    [Fact]
    public async Task ExportAsync_PreservesPolicyDeclarationsOnDefinitions()
    {
        await using var provider = PolicyTestFixtures.CreateCoreProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.DefinitionsOnly,
            DefinitionIds = ["Product"],
        });

        var definition = Assert.Single(result.Snapshot!.Definitions);
        Assert.Single(definition.PolicyDeclarations);
    }

    [Fact]
    public async Task SchemaUpgrade_WorksWhenDefinitionHasPolicyMetadata()
    {
        await using var provider = PolicyTestFixtures.CreateCoreProvider();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var schema = provider.GetRequiredService<IResourceSchemaVersionService>();
        await definitions.RegisterDefinitionAsync(PolicyTestFixtures.ProductDefinition(PolicyTestFixtures.ArchivePolicy()));
        var resource = await manager.CreateAsync("Product", new CreateResourceRequest());
        await definitions.RegisterDefinitionAsync(PolicyTestFixtures.ProductDefinition(PolicyTestFixtures.ArchivePolicy()));

        var result = await schema.UpgradeAsync(resource.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = resource.Version,
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Equal(2, result.Resource!.DefinitionVersion);
    }

    [Fact]
    public async Task LifecycleHooks_StillRunWhenPolicyMetadataExists()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<Recorder>()
            .AddResourceLifecycleHook<RecordingHook>()
            .BuildServiceProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);

        await provider.GetRequiredService<IResourceManager>().CreateAsync("Product", new CreateResourceRequest());

        Assert.Equal(1, provider.GetRequiredService<Recorder>().BeforeSaveCount);
    }

    private sealed class Recorder
    {
        public int BeforeSaveCount { get; set; }
    }

    private sealed class RecordingHook(Recorder recorder) : ResourceLifecycleHook
    {
        public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
            ResourceSaveLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            recorder.BeforeSaveCount++;
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }
    }
}
