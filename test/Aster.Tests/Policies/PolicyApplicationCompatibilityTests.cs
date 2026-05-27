using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyApplicationCompatibilityTests
{
    [Fact]
    public void AddAsterCore_RegistersPolicyApplicationService()
    {
        using var provider = PolicyTestFixtures.CreateCoreProvider();

        Assert.NotNull(provider.GetRequiredService<IResourcePolicyApplicationService>());
    }

    [Fact]
    public async Task PreviewAndResourceWrites_DoNotApplyPolicyMarkersAutomatically()
    {
        await using var provider = PolicyTestFixtures.CreateCoreProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            EvaluationTimestamp = new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Null(await provider.GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync("product-1", Aster.Core.Models.Tenancy.TenantScope.Default));
    }

    [Fact]
    public async Task ApplyAsync_DoesNotInvokeLifecycleHooks()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<Recorder>()
            .AddResourceLifecycleHook<RecordingHook>()
            .BuildServiceProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await provider.GetRequiredService<IResourceManager>().CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = "product-1",
        });
        var recorder = provider.GetRequiredService<Recorder>();
        Assert.Equal(1, recorder.BeforeSaveCount);

        await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = [PolicyTestFixtures.ApplicationCandidate("product-1")],
        });

        Assert.Equal(1, recorder.BeforeSaveCount);
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
