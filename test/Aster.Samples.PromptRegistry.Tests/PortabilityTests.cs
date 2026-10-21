using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Aster.Samples.PromptRegistry.Model;
using static Aster.Samples.PromptRegistry.PromptRegistryScenario;
using static Aster.Samples.PromptRegistry.Tests.RegistryTestData;

namespace Aster.Samples.PromptRegistry.Tests;

/// <summary>
/// A prompt set (an agent with the prompt and tools it references) moves from one environment to
/// another as a JSON snapshot; colliding items are remapped and references follow them.
/// </summary>
public sealed class PortabilityTests
{
    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Export_IncludesTheReferencedPromptAndToolsWithTheirChannels(string provider)
    {
        await using var eu = await StartAsync(provider, "eu");
        await SeedProductionAsync(eu.Registry);
        await ReleaseV2ToCandidateAsync(eu.Registry);
        await eu.Registry.SaveToolAsync("tool.unused", new Tool { Name = "unused", Description = "Not referenced by the agent." });

        var snapshot = JsonSerializer.Deserialize<PortableSnapshot>(await eu.Transfer.ExportAsync([AgentId]), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(
            new[] { AgentId, PromptId, PromptId, TicketToolId, KnowledgeBaseToolId }.Order(),
            snapshot.Resources.Select(static resource => resource.ResourceId).Order());
        Assert.Contains(snapshot.ActivationStates, static state => state is { ResourceId: PromptId, Channel: Channels.Candidate } && state.ActiveVersions.SequenceEqual([2]));
        Assert.Equal(
            new[] { RegistryDefinitions.AgentId, RegistryDefinitions.PromptId, RegistryDefinitions.ToolId },
            snapshot.Definitions.Select(static definition => definition.DefinitionId).Order());
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Import_IntoAnEmptyEnvironment_PreservesIdsAndChannels(string provider)
    {
        await using var eu = await StartAsync(provider, "eu");
        await SeedProductionAsync(eu.Registry);
        await using var us = await StartAsync(provider, "us");

        var result = await us.Transfer.ImportAsync(await eu.Transfer.ExportAsync([AgentId]));

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.DoesNotContain(result.IdentityMap, static mapping => mapping.Reason == PortableIdentityMappingReason.RemappedDivergent);
        var resolved = await us.Registry.ResolveAsync(AgentId, Channels.Production, "s");
        Assert.Equal(PromptId, resolved.PromptId);
        AssertSamePrompt(TriageV1, resolved.Prompt);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Import_OverACollidingPrompt_RemapsItAndRewritesTheAgentReference(string provider)
    {
        await using var eu = await StartAsync(provider, "eu");
        await SeedProductionAsync(eu.Registry);
        await ReleaseV2ToCandidateAsync(eu.Registry);
        await eu.Registry.PromoteToProductionAsync(PromptId);

        await using var us = await StartAsync(provider, "us");
        var local = await us.Registry.SavePromptAsync(PromptId, UsLocalTriage);
        await us.Registry.ReleaseToCandidateAsync(PromptId, local.Version);
        await us.Registry.PromoteToProductionAsync(PromptId);
        var snapshot = await eu.Transfer.ExportAsync([AgentId]);

        var preview = await us.Transfer.PreviewAsync(snapshot);
        var remapped = Assert.Single(preview.IdentityMap, static mapping =>
            mapping is { EntityKind: PortableEntityKind.ReferenceValue, Reason: PortableIdentityMappingReason.RemappedDivergent });
        Assert.Equal((PromptId, $"resources/{AgentId}/1/Agent/Prompt"), (remapped.SourceId, remapped.Location));
        Assert.NotEqual(PromptId, remapped.TargetId);

        // Definitions registered from the same code in both environments are reused, not remapped.
        Assert.All(
            preview.IdentityMap.Where(static mapping => mapping.EntityKind == PortableEntityKind.DefinitionVersion),
            static mapping => Assert.Equal(PortableIdentityMappingReason.ReusedIdentical, mapping.Reason));

        var result = await us.Transfer.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        var resolved = await us.Registry.ResolveAsync(AgentId, Channels.Production, "s");
        Assert.Equal((remapped.TargetId, 2), (resolved.PromptId, resolved.PromptVersion));
        AssertSamePrompt(TriageV2, resolved.Prompt);
        AssertSamePrompt(UsLocalTriage, await us.Registry.ResolvePromptAsync(PromptId, Channels.Production, "s"));

        var prompts = await us.Services.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery { DefinitionId = RegistryDefinitions.PromptId });
        Assert.Equal(new[] { PromptId, remapped.TargetId }.Order(), prompts.Select(static prompt => prompt.ResourceId).Order());
    }
}
