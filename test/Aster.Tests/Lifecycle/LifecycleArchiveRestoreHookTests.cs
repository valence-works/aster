using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

/// <summary>
/// The archive and restore lifecycle points (#116): paired before/after calls around each marker write, with the
/// outcome in <see cref="ResourceLifecycleContext.OperationStatus"/>.
/// </summary>
public sealed class LifecycleArchiveRestoreHookTests : IAsyncDisposable
{
    private static readonly DateTimeOffset MarkedAt = new(2026, 5, 27, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider provider;
    private readonly LifecycleHookRecorder recorder;
    private readonly SwitchableLifecycleMarkerStore store;
    private readonly HashSet<TenantScope> registeredTenants = [];

    public LifecycleArchiveRestoreHookTests()
    {
        var services = new ServiceCollection()
            .AddSingleton<LifecycleHookRecorder>()
            .AddAsterCore()
            .AddResourceLifecycleHook<FirstRecordingHook>()
            .AddResourceLifecycleHook<SecondRecordingHook>();
        services.AddSingleton(sp => new SwitchableLifecycleMarkerStore(sp.GetRequiredService<InMemoryResourceLifecycleMarkerStore>()));
        services.AddSingleton<IResourceLifecycleMarkerStore>(sp => sp.GetRequiredService<SwitchableLifecycleMarkerStore>());
        provider = services.BuildServiceProvider();
        recorder = provider.GetRequiredService<LifecycleHookRecorder>();
        store = provider.GetRequiredService<SwitchableLifecycleMarkerStore>();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    private IResourceLifecycleMarkerService Markers => provider.GetRequiredService<IResourceLifecycleMarkerService>();

    private IResourceLifecycleRestoreService Restore => provider.GetRequiredService<IResourceLifecycleRestoreService>();

    [Fact]
    public async Task Archive_RunsBeforeAndAfterArchiveHooksInOrderAroundTheMarkerWrite()
    {
        await SaveProductAsync("product-1");

        var result = await ArchiveAsync("product-1");

        Assert.True(result.Succeeded);
        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeArchive),
                ("second", LifecyclePoint.BeforeArchive),
                ("first", LifecyclePoint.AfterArchive),
                ("second", LifecyclePoint.AfterArchive),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var before = Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events[2].Context);
        LifecycleOperationAssert.Pending(before);
        LifecycleOperationAssert.Succeeded(after);
        Assert.Equal(before.OperationId, after.OperationId);
        Assert.Equal("product-1", after.ResourceId);
        Assert.Equal("Product", after.DefinitionId);
        Assert.Equal(TenantScope.Default, after.TenantScope);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, after.Marker.State);
        Assert.Equal("old", after.Marker.Reason);
        Assert.Equal(result.Marker, after.Marker);
    }

    [Fact]
    public async Task SoftDelete_RunsTheArchivePointsWithTheSoftDeletedMarker()
    {
        await SaveProductAsync("product-1");

        await ArchiveAsync("product-1", ResourceLifecycleMarkerState.SoftDeleted);

        var after = Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events.Last().Context);
        Assert.Equal(LifecyclePoint.AfterArchive, after.LifecyclePoint);
        Assert.Equal(ResourceLifecycleMarkerState.SoftDeleted, after.Marker.State);
    }

    [Fact]
    public async Task Archive_ThatWritesNothing_RunsNoHooks()
    {
        await SaveProductAsync("product-1");
        await ArchiveAsync("product-1");
        recorder.Events.Clear();

        var already = await ArchiveAsync("product-1");
        var conflict = await ArchiveAsync("product-1", ResourceLifecycleMarkerState.SoftDeleted);
        var missing = await ArchiveAsync("missing");

        Assert.True(already.Succeeded);
        Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict, Assert.Single(conflict.Diagnostics).Code);
        Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound, Assert.Single(missing.Diagnostics).Code);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task BeforeArchiveRejection_WritesNoMarkerRunsNoAfterHooksAndReportsTheHookCode()
    {
        await SaveProductAsync("product-1");
        recorder.RejectAt = LifecyclePoint.BeforeArchive;

        var result = await ArchiveAsync("product-1");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("test-rejected", diagnostic.Code);
        Assert.Equal("lifecycle/beforeArchive", diagnostic.Path);
        Assert.Equal("product-1", diagnostic.ResourceId);
        Assert.Null(result.Marker);
        Assert.Null(await Markers.GetAsync("product-1"));
        Assert.Equal([("first", LifecyclePoint.BeforeArchive)], recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());
    }

    [Fact]
    public async Task ArchiveWriteFailure_RunsAfterArchiveHooksWithFailedOutcomeAndRethrows()
    {
        await SaveProductAsync("product-1");
        store.ThrowSave = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveAsync("product-1"));

        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeArchive),
                ("second", LifecyclePoint.BeforeArchive),
                ("first", LifecyclePoint.AfterArchive),
                ("second", LifecyclePoint.AfterArchive),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());
        var after = Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events[2].Context);
        var failure = LifecycleOperationAssert.Failed(after, LifecycleOperationFailure.OperationFailedCode);
        Assert.Same(exception, failure.Exception);
        Assert.Equal(recorder.Events[0].OperationId, after.OperationId);
        Assert.Null(await Markers.GetAsync("product-1"));
    }

    [Fact]
    public async Task AfterArchiveHookFailure_KeepsTheMarkerAndReportsTheFailure()
    {
        await SaveProductAsync("product-1");
        recorder.FailAt = LifecyclePoint.AfterArchive;

        var result = await ArchiveAsync("product-1");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(LifecycleHookException.FailedCode, diagnostic.Code);
        Assert.Equal("lifecycle/afterArchive", diagnostic.Path);
        Assert.NotNull(result.Marker);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, (await Markers.GetAsync("product-1"))!.State);
    }

    [Fact]
    public async Task Restore_RunsBeforeAndAfterRestoreHooksInOrderAroundTheMarkerClear()
    {
        await SaveProductAsync("product-1");
        var archived = (await ArchiveAsync("product-1")).Marker;
        recorder.Events.Clear();

        var result = await RestoreAsync("product-1");

        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Restored, Assert.Single(result.Candidates).Status);
        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeRestore),
                ("second", LifecyclePoint.BeforeRestore),
                ("first", LifecyclePoint.AfterRestore),
                ("second", LifecyclePoint.AfterRestore),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var before = Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events[2].Context);
        LifecycleOperationAssert.Pending(before);
        LifecycleOperationAssert.Succeeded(after);
        Assert.Equal(before.OperationId, after.OperationId);
        Assert.Equal("Product", after.DefinitionId);
        Assert.Equal(archived, after.Marker);
        Assert.Null(await Markers.GetAsync("product-1"));
    }

    [Fact]
    public async Task RestorePreviewAndAlreadyRestored_RunNoHooks()
    {
        await SaveProductAsync("product-1");
        await SaveProductAsync("product-2");
        await ArchiveAsync("product-1");
        recorder.Events.Clear();

        var preview = await Restore.PreviewRestoreAsync(Request("product-1"));
        var already = await RestoreAsync("product-2");

        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Restorable, Assert.Single(preview.Candidates).Status);
        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.AlreadyRestored, Assert.Single(already.Candidates).Status);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task BeforeRestoreRejection_KeepsTheMarkerAndFailsTheCandidate()
    {
        await SaveProductAsync("product-1");
        await ArchiveAsync("product-1");
        recorder.Events.Clear();
        recorder.RejectAt = LifecyclePoint.BeforeRestore;

        var result = await RestoreAsync("product-1");

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Failed, candidate.Status);
        var diagnostic = Assert.Single(candidate.Diagnostics);
        Assert.Equal("test-rejected", diagnostic.Code);
        Assert.Equal("lifecycle/beforeRestore", diagnostic.Path);
        Assert.NotNull(await Markers.GetAsync("product-1"));
        Assert.Equal([("first", LifecyclePoint.BeforeRestore)], recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());
    }

    [Fact]
    public async Task RestoreWriteFailure_RunsAfterRestoreHooksWithFailedOutcomeAndRethrows()
    {
        await SaveProductAsync("product-1");
        await ArchiveAsync("product-1");
        recorder.Events.Clear();
        store.ThrowClear = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => RestoreAsync("product-1"));

        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeRestore),
                ("second", LifecyclePoint.BeforeRestore),
                ("first", LifecyclePoint.AfterRestore),
                ("second", LifecyclePoint.AfterRestore),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());
        var failure = LifecycleOperationAssert.Failed(recorder.Events[2].Context, LifecycleOperationFailure.OperationFailedCode);
        Assert.Same(exception, failure.Exception);
        Assert.NotNull(await Markers.GetAsync("product-1"));
    }

    [Fact]
    public async Task RestoreThatClearsNothing_RunsAfterRestoreHooksWithFailedOutcome()
    {
        await SaveProductAsync("product-1");
        await ArchiveAsync("product-1");
        recorder.Events.Clear();
        store.ClearReturnsFalse = true;

        await RestoreAsync("product-1");

        Assert.Equal(LifecyclePoint.AfterRestore, recorder.Events.Last().LifecyclePoint);
        LifecycleOperationAssert.Failed(recorder.Events.Last().Context, LifecycleOperationFailure.OperationFailedCode);
    }

    [Fact]
    public async Task PolicyApplication_RunsTheArchivePointsForEachMarkerItWrites()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");

        var result = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = MarkedAt,
            Candidates = [PolicyTestFixtures.ApplicationCandidate("product-1")],
        });

        Assert.Equal(ResourcePolicyApplicationCandidateStatus.Applied, Assert.Single(result.Candidates).Status);
        Assert.Equal(
            [LifecyclePoint.BeforeArchive, LifecyclePoint.BeforeArchive, LifecyclePoint.AfterArchive, LifecyclePoint.AfterArchive],
            recorder.Events.Select(static e => e.LifecyclePoint).ToList());
        var after = Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events.Last().Context);
        LifecycleOperationAssert.Succeeded(after);
        Assert.Equal("Product", after.DefinitionId);
    }

    [Fact]
    public async Task ArchiveInATenant_RunsHooksWithThatTenantScope()
    {
        var tenant = TenantScope.FromTenantId("tenant-a");
        await SaveProductAsync("product-1", tenant);

        await Markers.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            TenantScope = tenant,
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = MarkedAt,
        });

        Assert.All(recorder.Events, e => Assert.Equal(tenant, e.Context.TenantScope));
        Assert.Equal(tenant, Assert.IsType<ResourceArchiveLifecycleContext>(recorder.Events.Last().Context).Marker.TenantScope);
    }

    private async Task SaveProductAsync(string resourceId, TenantScope? tenant = null)
    {
        if (registeredTenants.Add(tenant ?? TenantScope.Default))
            await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, tenant);

        await PolicyTestFixtures.SaveResourceAsync(provider, resourceId, tenantScope: tenant);
    }

    private async Task<ResourceLifecycleMarkerResult> ArchiveAsync(
        string resourceId,
        ResourceLifecycleMarkerState state = ResourceLifecycleMarkerState.Archived) =>
        await Markers.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = resourceId,
            State = state,
            MarkedAt = MarkedAt,
            Reason = "old",
        });

    private async Task<ResourceLifecycleRestoreApplicationResult> RestoreAsync(string resourceId) =>
        await Restore.RestoreAsync(Request(resourceId));

    private static ResourceLifecycleRestoreRequest Request(string resourceId) =>
        new()
        {
            RestoredAt = MarkedAt,
            Candidates = [new ResourceLifecycleRestoreCandidate { ResourceId = resourceId, ExpectedState = ResourceLifecycleMarkerState.Archived }],
        };
}

/// <summary>A lifecycle marker store whose writes can be made to fail.</summary>
internal sealed class SwitchableLifecycleMarkerStore(InMemoryResourceLifecycleMarkerStore inner) : IResourceLifecycleMarkerClearStore
{
    public bool ThrowSave { get; set; }

    public bool ThrowClear { get; set; }

    public bool ClearReturnsFalse { get; set; }

    public ValueTask<ResourceLifecycleMarker?> GetMarkerAsync(string resourceId, TenantScope tenantScope, CancellationToken cancellationToken = default) =>
        inner.GetMarkerAsync(resourceId, tenantScope, cancellationToken);

    public ValueTask<IReadOnlyDictionary<string, ResourceLifecycleMarker>> GetMarkersAsync(
        IEnumerable<string> resourceIds,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default) =>
        inner.GetMarkersAsync(resourceIds, tenantScope, cancellationToken);

    public ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(ResourceLifecycleMarker marker, CancellationToken cancellationToken = default) =>
        ThrowSave ? throw new InvalidOperationException("simulated marker save failure") : inner.SaveMarkerAsync(marker, cancellationToken);

    public ValueTask<ResourceLifecycleMarkerCreateResult> CreateMarkerAsync(ResourceLifecycleMarker marker, CancellationToken cancellationToken = default) =>
        ThrowSave ? throw new InvalidOperationException("simulated marker save failure") : inner.CreateMarkerAsync(marker, cancellationToken);

    public ValueTask<bool> ClearMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        ResourceLifecycleMarkerState expectedState,
        CancellationToken cancellationToken = default)
    {
        if (ThrowClear)
            throw new InvalidOperationException("simulated marker clear failure");

        return ClearReturnsFalse ? ValueTask.FromResult(false) : inner.ClearMarkerAsync(resourceId, tenantScope, expectedState, cancellationToken);
    }
}
