using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;
using Aster.Tests.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityLifecycleHookTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceManager manager;
    private readonly IResourcePortabilityService portability;
    private readonly LifecycleHookRecorder recorder;

    public PortabilityLifecycleHookTests()
    {
        provider = new ServiceCollection()
            .AddSingleton<LifecycleHookRecorder>()
            .AddAsterCore()
            .AddResourceLifecycleHook<FirstRecordingHook>()
            .AddResourceLifecycleHook<SecondRecordingHook>()
            .BuildServiceProvider();

        definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        manager = provider.GetRequiredService<IResourceManager>();
        portability = provider.GetRequiredService<IResourcePortabilityService>();
        recorder = provider.GetRequiredService<LifecycleHookRecorder>();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task ExportAsync_RunsHooksInOrderWithContext()
    {
        var resource = await CreateResourceAsync();
        recorder.Events.Clear();

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [resource.ResourceId],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });

        Assert.NotNull(result.Snapshot);
        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeExport),
                ("second", LifecyclePoint.BeforeExport),
                ("first", LifecyclePoint.AfterExport),
                ("second", LifecyclePoint.AfterExport),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var before = Assert.IsType<ResourceExportLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceExportLifecycleContext>(recorder.Events[2].Context);
        Assert.Same(result.Snapshot, after.Snapshot);
        Assert.Same(result, after.ExportResult);
        Assert.Equal(before.OperationId, after.OperationId);
    }

    [Fact]
    public async Task ExportAsync_BeforeHookRejection_ReturnsDiagnostic()
    {
        await CreateResourceAsync();
        recorder.Events.Clear();
        recorder.RejectAt = LifecyclePoint.BeforeExport;

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["product-1"],
        });

        Assert.Null(result.Snapshot);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.LifecycleHookRejected, diagnostic.Code);
        Assert.Equal(PortableDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task PreviewImportAsync_RunsHooksAndDoesNotMutateStore()
    {
        var snapshot = CreateSnapshot();

        var preview = await portability.PreviewImportAsync(snapshot);

        Assert.True(preview.CanImport);
        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforePreviewImport),
                ("second", LifecyclePoint.BeforePreviewImport),
                ("first", LifecyclePoint.AfterPreviewImport),
                ("second", LifecyclePoint.AfterPreviewImport),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var after = Assert.IsType<ResourceImportLifecycleContext>(recorder.Events[2].Context);
        Assert.Same(preview, after.Preview);
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));
    }

    [Fact]
    public async Task ImportAsync_RunsHooksAndAppliesSnapshot()
    {
        var snapshot = CreateSnapshot();

        var result = await portability.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeImport),
                ("second", LifecyclePoint.BeforeImport),
                ("first", LifecyclePoint.AfterImport),
                ("second", LifecyclePoint.AfterImport),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var after = Assert.IsType<ResourceImportLifecycleContext>(recorder.Events[2].Context);
        Assert.Same(result, after.ImportResult);
        Assert.NotNull(await manager.GetLatestVersionAsync("product-1"));
    }

    [Fact]
    public async Task ImportAsync_BeforeRejectionReturnsDiagnosticWithoutMutation()
    {
        recorder.RejectAt = LifecyclePoint.BeforeImport;

        var result = await portability.ImportAsync(CreateSnapshot());

        Assert.Equal(PortableImportStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.LifecycleHookRejected, diagnostic.Code);
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));
    }

    [Fact]
    public async Task ImportAsync_AfterFailureReturnsDiagnosticWithoutRollbackClaim()
    {
        recorder.FailAt = LifecyclePoint.AfterImport;

        var result = await portability.ImportAsync(CreateSnapshot());

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.LifecycleHookFailed, diagnostic.Code);
        Assert.NotNull(await manager.GetLatestVersionAsync("product-1"));
    }

    private async ValueTask<Resource> CreateResourceAsync()
    {
        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());

        return await manager.CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = "product-1",
        });
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
}
