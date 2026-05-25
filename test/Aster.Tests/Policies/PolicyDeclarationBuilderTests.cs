using Aster.Core.Definitions;

namespace Aster.Tests.Policies;

public sealed class PolicyDeclarationBuilderTests
{
    [Fact]
    public void WithPolicy_AttachesPolicyDeclarationsToDefinition()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithPolicy(PolicyTestFixtures.ArchivePolicy())
            .WithPolicy(PolicyTestFixtures.PruningPolicy())
            .Build();

        Assert.Equal(2, definition.PolicyDeclarations.Count);
        Assert.Equal(["archive-old", "keep-latest"], definition.PolicyDeclarations.Select(static policy => policy.PolicyId).ToList());
    }
}
