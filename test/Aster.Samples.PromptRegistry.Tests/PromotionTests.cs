using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Samples.PromptRegistry.Model;
using static Aster.Samples.PromptRegistry.PromptRegistryScenario;
using static Aster.Samples.PromptRegistry.Tests.RegistryTestData;

namespace Aster.Samples.PromptRegistry.Tests;

/// <summary>
/// A saved version is a draft; releasing it makes it the version under evaluation in
/// <c>candidate</c>; promoting moves the candidate version to <c>production</c>.
/// </summary>
public sealed class PromotionTests
{
    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Draft_CandidateThenProduction(string provider)
    {
        await using var environment = await StartAsync(provider);
        var registry = environment.Registry;
        await SeedDraftsAsync(registry);

        // Drafts are active nowhere, so nothing resolves.
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ResolveAsync(AgentId, Channels.Candidate, "s"));

        foreach (var resourceId in new[] { KnowledgeBaseToolId, TicketToolId, PromptId, AgentId })
            await registry.ReleaseToCandidateAsync(resourceId, 1);

        var candidate = await registry.ResolveAsync(AgentId, Channels.Candidate, "s");
        Assert.Equal((1, PromptId, 1), (candidate.AgentVersion, candidate.PromptId, candidate.PromptVersion));
        Assert.Equal(new[] { "search_kb", "create_ticket" }, candidate.Tools.Select(static tool => tool.Tool.Name));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ResolveAsync(AgentId, Channels.Production, "s"));

        foreach (var resourceId in new[] { KnowledgeBaseToolId, TicketToolId, PromptId, AgentId })
            Assert.Equal(1, await registry.PromoteToProductionAsync(resourceId));

        var production = await registry.ResolveAsync(AgentId, Channels.Production, "s");
        AssertSamePrompt(TriageV1, production.Prompt);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task NewVersion_IsEvaluatedInCandidateWhileProductionKeepsTheOldOne(string provider)
    {
        await using var environment = await StartAsync(provider);
        var registry = environment.Registry;
        await SeedProductionAsync(registry);

        await ReleaseV2ToCandidateAsync(registry);

        Assert.Equal(2, (await registry.ResolveAsync(AgentId, Channels.Candidate, "s")).PromptVersion);
        Assert.Equal(1, (await registry.ResolveAsync(AgentId, Channels.Production, "s")).PromptVersion);

        await registry.PromoteToProductionAsync(PromptId);

        Assert.Equal([2], await registry.GetActiveVersionsAsync(PromptId, Channels.Production));
        AssertSamePrompt(TriageV2, (await registry.ResolveAsync(AgentId, Channels.Production, "s")).Prompt);
    }

    [Fact]
    public async Task PromoteToProduction_WithoutACandidate_Throws()
    {
        await using var environment = await StartAsync(InMemory);
        await SeedDraftsAsync(environment.Registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => environment.Registry.PromoteToProductionAsync(PromptId));
    }

    [Fact]
    public async Task SaveAgent_RejectsReferencesToMissingOrWrongTargets()
    {
        await using var environment = await StartAsync(InMemory);
        var registry = environment.Registry;
        await SeedDraftsAsync(registry);

        var missing = await Assert.ThrowsAsync<FacetValueValidationException>(() =>
            registry.SaveAgentAsync("agent.broken", SupportAgent with { Prompt = "prompt.does-not-exist" }));
        Assert.Equal(FacetValueDiagnosticCodes.ReferenceTargetMissing, Assert.Single(missing.Diagnostics).Code);

        var wrongType = await Assert.ThrowsAsync<FacetValueValidationException>(() =>
            registry.SaveAgentAsync("agent.broken", SupportAgent with { Tools = [PromptId] }));
        Assert.Equal(FacetValueDiagnosticCodes.ReferenceTargetTypeMismatch, Assert.Single(wrongType.Diagnostics).Code);
    }

    [Fact]
    public async Task RenderPrompt_FillsDeclaredVariablesAndRequiresThemAll()
    {
        await using var environment = await StartAsync(InMemory);
        await SeedProductionAsync(environment.Registry);
        var agent = await environment.Registry.ResolveAsync(AgentId, Channels.Production, "s");

        Assert.Equal(
            "You are a support agent for Aster. Classify the request and answer it: Where are my invoices?",
            agent.RenderPrompt(new Dictionary<string, string> { ["product"] = "Aster", ["request"] = "Where are my invoices?" }));
        Assert.Throws<ArgumentException>(() => agent.RenderPrompt(new Dictionary<string, string> { ["product"] = "Aster" }));
    }
}
