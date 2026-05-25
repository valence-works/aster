using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyEvaluationTimestampTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task PreviewAsync_AgePolicyWithoutEvaluationTimestamp_ReturnsStableDiagnostic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "old-product", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var evaluation = provider.GetRequiredService<IResourcePolicyEvaluationService>();

        var preview = await evaluation.PreviewAsync(new ResourcePolicyEvaluationRequest());

        var diagnostic = Assert.Single(preview.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyEvaluationTimestampRequired, diagnostic.Code);
        Assert.Empty(preview.Candidates);
    }
}
