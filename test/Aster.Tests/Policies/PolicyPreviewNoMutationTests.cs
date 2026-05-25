using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyPreviewNoMutationTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task PreviewAsync_DoesNotWriteLifecycleMarkers()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "old-product", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var evaluation = provider.GetRequiredService<IResourcePolicyEvaluationService>();
        var markerStore = provider.GetRequiredService<IResourceLifecycleMarkerStore>();

        await evaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            EvaluationTimestamp = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Null(await markerStore.GetMarkerAsync("old-product", Aster.Core.Models.Tenancy.TenantScope.Default));
    }
}
