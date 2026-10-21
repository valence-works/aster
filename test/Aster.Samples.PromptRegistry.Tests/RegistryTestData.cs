using Aster.Samples.PromptRegistry.Model;
using Aster.Samples.PromptRegistry.Registry;
using static Aster.Samples.PromptRegistry.PromptRegistryScenario;

namespace Aster.Samples.PromptRegistry.Tests;

internal static class RegistryTestData
{
    public const string InMemory = "in-memory";
    public const string Sqlite = "sqlite";

    public static Task<RegistryEnvironment> StartAsync(string provider, string name = "test") =>
        RegistryEnvironment.StartAsync(name, useSqlite: provider == Sqlite);

    /// <summary>Saves the tools, prompt v1 and the agent as drafts.</summary>
    public static async Task SeedDraftsAsync(PromptRegistryService registry)
    {
        await registry.SaveToolAsync(KnowledgeBaseToolId, new Tool { Name = "search_kb", Description = "Search the knowledge base." });
        await registry.SaveToolAsync(TicketToolId, new Tool { Name = "create_ticket", Description = "Open a support ticket." });
        await registry.SavePromptAsync(PromptId, TriageV1);
        await registry.SaveAgentAsync(AgentId, SupportAgent);
    }

    /// <summary>Seeds the drafts and moves every v1 through candidate into production.</summary>
    public static async Task SeedProductionAsync(PromptRegistryService registry)
    {
        await SeedDraftsAsync(registry);
        foreach (var resourceId in new[] { KnowledgeBaseToolId, TicketToolId, PromptId, AgentId })
        {
            await registry.ReleaseToCandidateAsync(resourceId, 1);
            await registry.PromoteToProductionAsync(resourceId);
        }
    }

    /// <summary>Asserts two prompts have the same content (records compare arrays by reference).</summary>
    public static void AssertSamePrompt(Prompt expected, Prompt actual)
    {
        Assert.Equal((expected.Name, expected.Template, expected.Model), (actual.Name, actual.Template, actual.Model));
        Assert.Equal(expected.Variables, actual.Variables);
    }

    /// <summary>Saves prompt v2 and releases it to candidate.</summary>
    public static async Task ReleaseV2ToCandidateAsync(PromptRegistryService registry)
    {
        var v2 = await registry.SavePromptAsync(PromptId, TriageV2);
        await registry.ReleaseToCandidateAsync(PromptId, v2.Version);
    }
}
