using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyApplicationServiceTests : IDisposable
{
    private static readonly DateTimeOffset AppliedAt = new(2026, 5, 27, 12, 30, 0, TimeSpan.Zero);
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_AppliesArchiveAndSoftDeleteCandidates()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies:
            [
                PolicyTestFixtures.ArchivePolicy("archive"),
                PolicyTestFixtures.SoftDeletePolicy("soft-delete"),
            ]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "archive-target");
        await PolicyTestFixtures.SaveResourceAsync(provider, "soft-delete-target");
        var application = provider.GetRequiredService<IResourcePolicyApplicationService>();

        var result = await application.ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = AppliedAt,
            Candidates =
            [
                PolicyTestFixtures.ApplicationCandidate("archive-target", "archive"),
                PolicyTestFixtures.ApplicationCandidate(
                    "soft-delete-target",
                    "soft-delete",
                    ResourcePolicyKind.SoftDelete,
                    ResourcePolicyOutcome.SoftDelete),
            ],
        });

        Assert.Equal(2, result.AppliedCount);
        Assert.All(result.Candidates, candidate => Assert.Equal(ResourcePolicyApplicationCandidateStatus.Applied, candidate.Status));

        var markers = provider.GetRequiredService<IResourceLifecycleMarkerStore>();
        var archived = await markers.GetMarkerAsync("archive-target", Aster.Core.Models.Tenancy.TenantScope.Default);
        var softDeleted = await markers.GetMarkerAsync("soft-delete-target", Aster.Core.Models.Tenancy.TenantScope.Default);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, archived!.State);
        Assert.Equal(ResourceLifecycleMarkerState.SoftDeleted, softDeleted!.State);
    }

    [Fact]
    public async Task ApplyAsync_AppliesOnlySelectedCandidatesAndDoesNotRewriteResourceVersions()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "selected");
        await PolicyTestFixtures.SaveResourceAsync(provider, "unselected");
        var application = provider.GetRequiredService<IResourcePolicyApplicationService>();

        await application.ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = AppliedAt,
            Candidates = [PolicyTestFixtures.ApplicationCandidate("selected")],
        });

        var markers = provider.GetRequiredService<IResourceLifecycleMarkerStore>();
        Assert.NotNull(await markers.GetMarkerAsync("selected", Aster.Core.Models.Tenancy.TenantScope.Default));
        Assert.Null(await markers.GetMarkerAsync("unselected", Aster.Core.Models.Tenancy.TenantScope.Default));

        var versions = (await provider.GetRequiredService<IResourceVersionReader>().ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
        })).ToList();
        Assert.Equal(2, versions.Count);
        Assert.All(versions, version => Assert.Equal(1, version.Version));
    }
}
