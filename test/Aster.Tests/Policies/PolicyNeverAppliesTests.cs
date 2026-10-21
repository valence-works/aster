using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

/// <summary>
/// #189: validation rejects policies whose criteria match only targets the outcome can never change, so previews
/// stop proposing candidates that every application refuses or leaves as they are.
/// </summary>
public sealed class PolicyNeverAppliesTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();
    private readonly IResourcePolicyValidator validator;

    public PolicyNeverAppliesTests() => validator = provider.GetRequiredService<IResourcePolicyValidator>();

    public void Dispose() => provider.Dispose();

    [Theory]
    [InlineData(ResourcePolicyOutcome.Archive, ResourceLifecycleMarkerState.Archived)]
    [InlineData(ResourcePolicyOutcome.Archive, ResourceLifecycleMarkerState.SoftDeleted)]
    [InlineData(ResourcePolicyOutcome.SoftDelete, ResourceLifecycleMarkerState.Archived)]
    [InlineData(ResourcePolicyOutcome.SoftDelete, ResourceLifecycleMarkerState.SoftDeleted)]
    public void Validate_MarkerOutcomeOnMarkedResources_NeverApplies(
        ResourcePolicyOutcome outcome,
        ResourceLifecycleMarkerState lifecycleState)
    {
        var result = validator.Validate(PolicyTestFixtures.ProductDefinition(MarkerPolicy(outcome, lifecycleState)));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyNeverApplies, diagnostic.Code);
        Assert.Equal("policyDeclarations/0/criteria/lifecycleState", diagnostic.Path);
        Assert.Equal("marker-policy", diagnostic.PolicyId);
    }

    [Theory]
    [InlineData(ResourcePolicyOutcome.Archive, null)]
    [InlineData(ResourcePolicyOutcome.Archive, ResourceLifecycleMarkerState.None)]
    [InlineData(ResourcePolicyOutcome.SoftDelete, null)]
    [InlineData(ResourcePolicyOutcome.SoftDelete, ResourceLifecycleMarkerState.None)]
    public void Validate_MarkerOutcomeOnUnmarkedResources_IsValid(
        ResourcePolicyOutcome outcome,
        ResourceLifecycleMarkerState? lifecycleState)
    {
        Assert.True(validator.Validate(PolicyTestFixtures.ProductDefinition(MarkerPolicy(outcome, lifecycleState))).IsValid);
    }

    [Theory]
    [InlineData(ResourceLifecycleMarkerState.Archived)]
    [InlineData(ResourceLifecycleMarkerState.SoftDeleted)]
    public void Validate_OutcomesThatWriteNoMarker_AcceptMarkedResources(ResourceLifecycleMarkerState lifecycleState)
    {
        var retain = new ResourcePolicyDeclaration
        {
            PolicyId = "retain-marked",
            Kind = ResourcePolicyKind.Retention,
            Target = ResourcePolicyTarget.Resource,
            Outcome = ResourcePolicyOutcome.Retain,
            Criteria = new ResourcePolicyCriteria { LifecycleState = lifecycleState },
        };
        var prune = PolicyTestFixtures.PruningPolicy("prune-marked", lifecycleState: lifecycleState);

        Assert.True(validator.Validate(PolicyTestFixtures.ProductDefinition(retain, prune)).IsValid);
    }

    [Fact]
    public void Validate_PruningOnlyActiveVersions_NeverApplies()
    {
        var policy = PolicyTestFixtures.PruningPolicy("prune-active") with
        {
            Criteria = new ResourcePolicyCriteria
            {
                MaximumRetainedVersions = 1,
                ActivationState = ResourcePolicyActivationState.Active,
                ActivationChannel = "Published",
            },
        };

        var diagnostic = Assert.Single(validator.Validate(PolicyTestFixtures.ProductDefinition(policy)).Diagnostics);

        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyNeverApplies, diagnostic.Code);
        Assert.Equal("policyDeclarations/0/criteria/activationState", diagnostic.Path);
        Assert.Equal("prune-active", diagnostic.PolicyId);
    }

    [Fact]
    public async Task PreviewAsync_SoftDeleteOfArchivedResources_ProposesNothingAndReportsWhy()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies: [MarkerPolicy(ResourcePolicyOutcome.SoftDelete, ResourceLifecycleMarkerState.Archived)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "archived");
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "archived",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });

        var preview = await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(
            new ResourcePolicyEvaluationRequest());

        Assert.Empty(preview.Candidates);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyNeverApplies, Assert.Single(preview.Diagnostics).Code);
    }

    private static ResourcePolicyDeclaration MarkerPolicy(
        ResourcePolicyOutcome outcome,
        ResourceLifecycleMarkerState? lifecycleState) =>
        new()
        {
            PolicyId = "marker-policy",
            Kind = outcome == ResourcePolicyOutcome.Archive ? ResourcePolicyKind.Archival : ResourcePolicyKind.SoftDelete,
            Target = ResourcePolicyTarget.Resource,
            Outcome = outcome,
            Criteria = new ResourcePolicyCriteria { LifecycleState = lifecycleState },
        };
}
