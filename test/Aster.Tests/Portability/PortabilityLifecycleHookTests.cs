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
        Assert.Equal("test-rejected", diagnostic.Code);
        Assert.Equal(PortableDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("lifecycle/beforeExport", diagnostic.Path);
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
        Assert.Equal("test-rejected", diagnostic.Code);
        Assert.Equal("lifecycle/beforeImport", diagnostic.Path);
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
        Assert.Equal("lifecycle/afterImport", diagnostic.Path);
        Assert.NotNull(await manager.GetLatestVersionAsync("product-1"));
    }

    [Fact]
    public async Task ImportAsync_PlanFailureRunsAfterImportHook()
    {
        await portability.ImportAsync(CreateSnapshotWithOwner("target-owner"));
        recorder.Events.Clear();

        var result = await portability.ImportAsync(
            CreateSnapshotWithOwner("incoming-owner"),
            new PortableImportOptions { CollisionMode = PortableImportCollisionMode.Strict });

        Assert.Equal(PortableImportStatus.Failed, result.Status);
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
    }

    [Fact]
    public static async Task ImportAsync_ApplyFailureRunsAfterImportHook()
    {
        await using var scopedProvider = BuildThrowingApplyHookProvider();
        var scopedPortability = scopedProvider.GetRequiredService<IResourcePortabilityService>();
        var scopedRecorder = scopedProvider.GetRequiredService<LifecycleHookRecorder>();

        var result = await scopedPortability.ImportAsync(CreateSnapshot());

        Assert.Equal(PortableImportStatus.Failed, result.Status);
        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeImport),
                ("first", LifecyclePoint.AfterImport),
            ],
            scopedRecorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var after = Assert.IsType<ResourceImportLifecycleContext>(scopedRecorder.Events[1].Context);
        Assert.Same(result, after.ImportResult);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.ImportApplyFailed);
    }

    [Fact]
    public async Task ExportAsync_HookCannotMutateExportRequestUsedByOperation()
    {
        await using var scopedProvider = BuildMutatingHookProvider();
        var scopedDefinitionStore = scopedProvider.GetRequiredService<IResourceDefinitionStore>();
        var scopedManager = scopedProvider.GetRequiredService<IResourceManager>();
        var scopedPortability = scopedProvider.GetRequiredService<IResourcePortabilityService>();

        await scopedDefinitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());
        var resource = await scopedManager.CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = "product-1",
        });
        var request = new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [resource.ResourceId],
        };

        var result = await scopedPortability.ExportAsync(request);

        Assert.Equal(PortableExportScopeMode.SelectedResources, request.ScopeMode);
        Assert.Equal([resource.ResourceId], request.ResourceIds);
        var exported = Assert.Single(result.Snapshot!.Resources);
        Assert.Equal(resource.ResourceId, exported.ResourceId);
    }

    [Fact]
    public async Task PreviewAndImport_HookCannotMutateImportOptionsUsedByPlanning()
    {
        await using var scopedProvider = BuildMutatingHookProvider();
        var scopedManager = scopedProvider.GetRequiredService<IResourceManager>();
        var scopedPortability = scopedProvider.GetRequiredService<IResourcePortabilityService>();
        await scopedPortability.ImportAsync(CreateSnapshotWithOwner("target-owner"));
        var options = new PortableImportOptions { CollisionMode = PortableImportCollisionMode.Strict };
        var incoming = CreateSnapshotWithOwner("incoming-owner");

        var preview = await scopedPortability.PreviewImportAsync(incoming, options);
        var result = await scopedPortability.ImportAsync(incoming, options);

        Assert.Equal(PortableImportCollisionMode.Strict, options.CollisionMode);
        Assert.False(preview.CanImport);
        Assert.Contains(preview.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.DivergentIdentityCollision);
        Assert.Equal(PortableImportStatus.Failed, result.Status);
        Assert.Null(await scopedManager.GetLatestVersionAsync("product-1__imported"));
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

    private static PortableSnapshot CreateSnapshotWithOwner(string owner)
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

    private static ServiceProvider BuildMutatingHookProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .AddResourceLifecycleHook<MutatingPortabilityHook>()
            .BuildServiceProvider();

    private static ServiceProvider BuildThrowingApplyHookProvider() =>
        new ServiceCollection()
            .AddSingleton<LifecycleHookRecorder>()
            .AddAsterCore()
            .AddSingleton<IResourcePortabilityStore, ThrowingApplyPortabilityStore>()
            .AddResourceLifecycleHook<FirstRecordingHook>()
            .BuildServiceProvider();

    private sealed class MutatingPortabilityHook : ResourceLifecycleHook
    {
        public override ValueTask<LifecycleHookOutcome> OnBeforeExportAsync(
            ResourceExportLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            context.ExportRequest.ScopeMode = PortableExportScopeMode.DefinitionsOnly;
            context.ExportRequest.ResourceIds.Clear();
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }

        public override ValueTask<LifecycleHookOutcome> OnBeforePreviewImportAsync(
            ResourceImportLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            context.ImportOptions.CollisionMode = PortableImportCollisionMode.RemapDivergent;
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }

        public override ValueTask<LifecycleHookOutcome> OnBeforeImportAsync(
            ResourceImportLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            context.ImportOptions.CollisionMode = PortableImportCollisionMode.RemapDivergent;
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }
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
