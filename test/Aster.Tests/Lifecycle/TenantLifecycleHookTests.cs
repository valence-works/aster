using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Lifecycle;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class TenantLifecycleHookTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly TenantLifecycleRecorder recorder;

    public TenantLifecycleHookTests()
    {
        provider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<TenantLifecycleRecorder>()
            .AddResourceLifecycleHook<TenantRecordingHook>()
            .BuildServiceProvider();
        recorder = provider.GetRequiredService<TenantLifecycleRecorder>();
    }

    public async ValueTask DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task LifecycleHookContexts_CarryOperationTenantScope()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "tenant-product", "Tenant A", TenantScopeTestFixtures.TenantA);

        Assert.Contains(recorder.TenantIds, id => id == TenantScopeTestFixtures.TenantA.TenantId);
    }

    private sealed class TenantLifecycleRecorder
    {
        public List<string> TenantIds { get; } = [];
    }

    private sealed class TenantRecordingHook(TenantLifecycleRecorder recorder) : ResourceLifecycleHook
    {
        public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
            ResourceSaveLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            recorder.TenantIds.Add(context.TenantScope.TenantId);
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());
        }
    }
}
