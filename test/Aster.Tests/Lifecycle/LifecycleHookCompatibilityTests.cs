using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleHookCompatibilityTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    public LifecycleHookCompatibilityTests()
    {
        provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task AddAsterCore_WithNoHooks_PreservesLifecycleOperations()
    {
        Assert.Empty(provider.GetServices<IResourceLifecycleHook>());

        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var manager = provider.GetRequiredService<IResourceManager>();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        var created = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        var updated = await manager.UpdateAsync(
            created.ResourceId,
            LifecycleHookTestFixtures.UpdateRequest(created.Version));
        await manager.ActivateAsync(updated.ResourceId, updated.Version, LifecycleHookTestFixtures.Channel);

        var export = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [updated.ResourceId],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });
        Assert.NotNull(export.Snapshot);

        var preview = await portability.PreviewImportAsync(export.Snapshot);
        Assert.True(preview.CanImport);

        var import = await portability.ImportAsync(export.Snapshot);
        Assert.Equal(PortableImportStatus.NoOp, import.Status);
    }

    [Fact]
    public async Task AddAsterCore_HookMayDependOnResourceManager()
    {
        await using var scopedProvider = new ServiceCollection()
            .AddSingleton<ManagerHookRecorder>()
            .AddAsterCore()
            .AddResourceLifecycleHook<ManagerDependentHook>()
            .BuildServiceProvider();

        await LifecycleHookTestFixtures.SaveDefinitionAsync(scopedProvider);
        var manager = scopedProvider.GetRequiredService<IResourceManager>();

        await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());

        Assert.True(scopedProvider.GetRequiredService<ManagerHookRecorder>().WasInvoked);
    }

    [Fact]
    public async Task AddAsterCore_ManualScopedHookResolvesFromInvocationScope()
    {
        await using var scopedProvider = new ServiceCollection()
            .AddSingleton<ScopedHookRecorder>()
            .AddScoped<ScopedDependency>()
            .AddAsterCore()
            .AddScoped<ScopedManagerDependentHook>()
            .AddScoped<IResourceLifecycleHook>(sp => sp.GetRequiredService<ScopedManagerDependentHook>())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await LifecycleHookTestFixtures.SaveDefinitionAsync(scopedProvider);
        var manager = scopedProvider.GetRequiredService<IResourceManager>();

        await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());

        Assert.True(scopedProvider.GetRequiredService<ScopedHookRecorder>().WasInvoked);
    }

    [Fact]
    public async Task AddResourceLifecycleHook_DuplicateRegistrationsInvokeResolvableHookInstance()
    {
        await using var scopedProvider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceLifecycleHook<DuplicateRegistrationHook>()
            .AddResourceLifecycleHook<DuplicateRegistrationHook>()
            .BuildServiceProvider();

        await LifecycleHookTestFixtures.SaveDefinitionAsync(scopedProvider);
        var manager = scopedProvider.GetRequiredService<IResourceManager>();

        await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());

        var hook = scopedProvider.GetRequiredService<DuplicateRegistrationHook>();
        Assert.Equal(2, hook.Invocations);
    }


    private sealed class ManagerDependentHook(
        IResourceManager manager,
        ManagerHookRecorder recorder) : ResourceLifecycleHook
    {
        public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
            ResourceSaveLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.NotNull(manager);
            recorder.WasInvoked = true;
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }
    }

    private sealed class ManagerHookRecorder
    {
        public bool WasInvoked { get; set; }
    }

    private sealed class ScopedManagerDependentHook(
        ScopedDependency dependency,
        IResourceManager manager,
        ScopedHookRecorder recorder) : ResourceLifecycleHook
    {
        public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
            ResourceSaveLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.NotNull(dependency);
            Assert.NotNull(manager);
            recorder.WasInvoked = true;
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }
    }

    private sealed class ScopedDependency;

    private sealed class ScopedHookRecorder
    {
        public bool WasInvoked { get; set; }
    }

    private sealed class DuplicateRegistrationHook : ResourceLifecycleHook
    {
        public int Invocations { get; private set; }

        public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
            ResourceSaveLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            Invocations++;
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }
    }
}
