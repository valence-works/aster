using Aster.Samples.PromptRegistry.Model;
using Aster.Samples.PromptRegistry.Registry;
using static Aster.Samples.PromptRegistry.PromptRegistryScenario;
using static Aster.Samples.PromptRegistry.Tests.RegistryTestData;

namespace Aster.Samples.PromptRegistry.Tests;

/// <summary>
/// An A/B test keeps two versions active in <c>production</c> (<c>allowMultipleActive</c>); each
/// subject resolves to one of them, always the same one, until the test is concluded.
/// </summary>
public sealed class AbTestTests
{
    private static readonly string[] Subjects = Enumerable.Range(0, 400).Select(static index => $"user-{index}").ToArray();

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task AbTest_SplitsTrafficStickilyAndConcludesToTheWinner(string provider)
    {
        await using var environment = await StartAsync(provider);
        var registry = environment.Registry;
        await SeedProductionAsync(registry);
        await ReleaseV2ToCandidateAsync(registry);

        Assert.Equal([1, 2], await registry.StartAbTestAsync(PromptId));

        var assignments = new Dictionary<string, int>();
        foreach (var subject in Subjects)
            assignments[subject] = (await registry.ResolveAsync(AgentId, Channels.Production, subject)).PromptVersion;

        Assert.InRange(assignments.Values.Count(static version => version == 2), 150, 250);
        foreach (var subject in Subjects.Take(20))
            Assert.Equal(assignments[subject], (await registry.ResolveAsync(AgentId, Channels.Production, subject)).PromptVersion);
        Assert.All(Subjects, subject => Assert.Equal(VariantSelector.Select(PromptId, subject, 2) + 1, assignments[subject]));

        // Only the prompt is under test: the agent and tools have one active version each.
        Assert.Equal([1], await registry.GetActiveVersionsAsync(AgentId, Channels.Production));

        await registry.ConcludeAbTestAsync(PromptId, winner: 2);

        Assert.Equal([2], await registry.GetActiveVersionsAsync(PromptId, Channels.Production));
        foreach (var subject in Subjects.Take(20))
            Assert.Equal(2, (await registry.ResolveAsync(AgentId, Channels.Production, subject)).PromptVersion);
    }

    [Fact]
    public async Task ConcludeAbTest_KeepingTheIncumbent_DeactivatesTheChallenger()
    {
        await using var environment = await StartAsync(InMemory);
        var registry = environment.Registry;
        await SeedProductionAsync(registry);
        await ReleaseV2ToCandidateAsync(registry);
        await registry.StartAbTestAsync(PromptId);

        await registry.ConcludeAbTestAsync(PromptId, winner: 1);

        Assert.Equal([1], await registry.GetActiveVersionsAsync(PromptId, Channels.Production));
        Assert.Equal([2], await registry.GetActiveVersionsAsync(PromptId, Channels.Candidate));
    }

    [Fact]
    public async Task PromoteToProduction_DuringAnAbTest_EndsIt()
    {
        await using var environment = await StartAsync(InMemory);
        var registry = environment.Registry;
        await SeedProductionAsync(registry);
        await ReleaseV2ToCandidateAsync(registry);
        await registry.StartAbTestAsync(PromptId);

        await registry.PromoteToProductionAsync(PromptId);

        Assert.Equal([2], await registry.GetActiveVersionsAsync(PromptId, Channels.Production));
    }

    [Fact]
    public async Task AbTest_InvalidTransitions_Throw()
    {
        await using var environment = await StartAsync(InMemory);
        var registry = environment.Registry;
        await SeedDraftsAsync(registry);
        await registry.ReleaseToCandidateAsync(PromptId, 1);

        // Nothing in production to compare against.
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.StartAbTestAsync(PromptId));

        // The candidate is already the production version.
        await registry.PromoteToProductionAsync(PromptId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.StartAbTestAsync(PromptId));

        // The winner must be one of the arms.
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ConcludeAbTestAsync(PromptId, winner: 2));
    }
}
