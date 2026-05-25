using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyValidationTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();
    private readonly IResourcePolicyValidator validator;

    public PolicyValidationTests() => validator = provider.GetRequiredService<IResourcePolicyValidator>();

    public void Dispose() => provider.Dispose();

    [Fact]
    public void Validate_ValidPolicyDeclarations_ReturnsSuccess()
    {
        var definition = PolicyTestFixtures.ProductDefinition(
            PolicyTestFixtures.ArchivePolicy(),
            PolicyTestFixtures.SoftDeletePolicy(),
            PolicyTestFixtures.PruningPolicy());

        var result = validator.Validate(definition);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Validate_UnsupportedFacetPredicate_ReturnsStableDiagnostic()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithPolicy(PolicyTestFixtures.ArchivePolicy() with
            {
                Criteria = new ResourcePolicyCriteria
                {
                    UnsupportedFacetPredicate = "Color = 'Red'",
                },
            })
            .Build();

        var result = validator.Validate(definition);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyCriteriaUnsupported, diagnostic.Code);
    }

    [Fact]
    public void Validate_DuplicatePolicyIds_ReturnsConflictDiagnostic()
    {
        var definition = PolicyTestFixtures.ProductDefinition(
            PolicyTestFixtures.ArchivePolicy("duplicate"),
            PolicyTestFixtures.SoftDeletePolicy("duplicate"));

        var result = validator.Validate(definition);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.PolicyConflict);
    }

    [Fact]
    public void Validate_PrunePolicyWithoutRetainedVersionCount_ReturnsInvalidDiagnostic()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithPolicy(new ResourcePolicyDeclaration
            {
                PolicyId = "bad-prune",
                Kind = ResourcePolicyKind.VersionPruning,
                Target = ResourcePolicyTarget.ResourceVersion,
                Outcome = ResourcePolicyOutcome.PrunePreview,
            })
            .Build();

        var result = validator.Validate(definition);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.PolicyInvalid);
    }
}
