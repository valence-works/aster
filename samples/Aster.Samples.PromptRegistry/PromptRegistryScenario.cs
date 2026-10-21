using Microsoft.Extensions.DependencyInjection;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Samples.PromptRegistry.Model;
using Aster.Samples.PromptRegistry.Registry;

namespace Aster.Samples.PromptRegistry;

/// <summary>
/// The walkthrough <c>dotnet run</c> prints: draft, candidate and production; an A/B test with two
/// versions active in production; then a prompt set moved from the <c>eu</c> environment to <c>us</c>.
/// </summary>
public static class PromptRegistryScenario
{
    /// <summary>The agent the walkthrough builds.</summary>
    public const string AgentId = "agent.support";

    /// <summary>The agent's prompt.</summary>
    public const string PromptId = "prompt.support-triage";

    /// <summary>The knowledge base search tool.</summary>
    public const string KnowledgeBaseToolId = "tool.knowledge-base";

    /// <summary>The ticketing tool.</summary>
    public const string TicketToolId = "tool.create-ticket";

    /// <summary>Version 1 of the prompt.</summary>
    public static Prompt TriageV1 { get; } = new()
    {
        Name = "Support triage",
        Template = "You are a support agent for {{product}}. Classify the request and answer it: {{request}}",
        Variables = ["product", "request"],
        Model = new ModelSettings { Provider = "openai", Model = "gpt-4o-mini", Temperature = 0.2m, MaxTokens = 512 },
    };

    /// <summary>Version 2 of the prompt: asks for sources and runs warmer.</summary>
    public static Prompt TriageV2 { get; } = TriageV1 with
    {
        Template = "You are a support agent for {{product}}. Answer in three steps and cite knowledge base articles: {{request}}",
        Model = TriageV1.Model with { Temperature = 0.4m },
    };

    /// <summary>The prompt the <c>us</c> team authored locally under the same resource ID.</summary>
    public static Prompt UsLocalTriage { get; } = TriageV1 with { Name = "Support triage (US)", Template = "Help the customer with {{request}}.", Variables = ["request"] };

    /// <summary>The agent.</summary>
    public static Agent SupportAgent { get; } = new() { Name = "Support assistant", Prompt = PromptId, Tools = [KnowledgeBaseToolId, TicketToolId] };

    /// <summary>Runs the walkthrough and writes it to <paramref name="output"/>.</summary>
    public static async Task RunAsync(bool useSqlite, TextWriter output, CancellationToken cancellationToken = default)
    {
        await using var eu = await RegistryEnvironment.StartAsync("eu", useSqlite, cancellationToken);
        var registry = eu.Registry;

        output.WriteLine("1. Draft: save the tools, prompt v1 and the agent in the eu environment. Nothing is active yet.");
        await SeedAsync(registry, cancellationToken);
        await DescribeAsync(eu, output, cancellationToken);

        output.WriteLine("2. Release everything to candidate, evaluate it there, then promote it to production.");
        foreach (var resourceId in new[] { KnowledgeBaseToolId, TicketToolId, PromptId, AgentId })
            await registry.ReleaseToCandidateAsync(resourceId, 1, cancellationToken);
        output.WriteLine($"    candidate resolves {Describe(await registry.ResolveAsync(AgentId, Channels.Candidate, "eval-1", cancellationToken))}");
        foreach (var resourceId in new[] { KnowledgeBaseToolId, TicketToolId, PromptId, AgentId })
            await registry.PromoteToProductionAsync(resourceId, cancellationToken);
        await DescribeAsync(eu, output, cancellationToken);

        output.WriteLine("3. Save prompt v2 and release it to candidate. Production keeps serving v1.");
        var v2 = await registry.SavePromptAsync(PromptId, TriageV2, cancellationToken);
        await registry.ReleaseToCandidateAsync(PromptId, v2.Version, cancellationToken);
        output.WriteLine($"    candidate resolves {Describe(await registry.ResolveAsync(AgentId, Channels.Candidate, "eval-1", cancellationToken))}");
        output.WriteLine($"    production resolves {Describe(await registry.ResolveAsync(AgentId, Channels.Production, "conversation-1", cancellationToken))}");
        output.WriteLine();

        output.WriteLine("4. A/B test: activate v2 in production next to v1 (allowMultipleActive). Each conversation sticks to one arm.");
        var arms = await registry.StartAbTestAsync(PromptId, cancellationToken);
        output.WriteLine($"    production has prompt versions [{string.Join(", ", arms)}] active");
        foreach (var conversation in Enumerable.Range(1, 6).Select(static index => $"conversation-{index}"))
        {
            var first = await registry.ResolveAsync(AgentId, Channels.Production, conversation, cancellationToken);
            var again = await registry.ResolveAsync(AgentId, Channels.Production, conversation, cancellationToken);
            output.WriteLine($"    {conversation} -> prompt v{first.PromptVersion} (again: v{again.PromptVersion})");
        }

        var split = new int[2];
        for (var index = 0; index < 1000; index++)
            split[(await registry.ResolveAsync(AgentId, Channels.Production, $"user-{index}", cancellationToken)).PromptVersion - 1]++;
        output.WriteLine($"    1,000 conversations: {split[0]} on v1, {split[1]} on v2");
        output.WriteLine();

        output.WriteLine("5. v2 wins: conclude the test. v1 is deactivated in production.");
        await registry.ConcludeAbTestAsync(PromptId, v2.Version, cancellationToken);
        await DescribeAsync(eu, output, cancellationToken);
        var rendered = (await registry.ResolveAsync(AgentId, Channels.Production, "conversation-1", cancellationToken))
            .RenderPrompt(new Dictionary<string, string> { ["product"] = "Aster", ["request"] = "How do I roll back?" });
        output.WriteLine($"    rendered: {rendered}");
        output.WriteLine();

        output.WriteLine("6. Move the prompt set to the us environment, where the team already has its own prompt.support-triage.");
        await using var us = await RegistryEnvironment.StartAsync("us", useSqlite, cancellationToken);
        var local = await us.Registry.SavePromptAsync(PromptId, UsLocalTriage, cancellationToken);
        await us.Registry.ReleaseToCandidateAsync(PromptId, local.Version, cancellationToken);
        await us.Registry.PromoteToProductionAsync(PromptId, cancellationToken);

        var snapshot = await eu.Transfer.ExportAsync([AgentId], cancellationToken);
        output.WriteLine($"    exported {AgentId} with the prompt and tools it references ({snapshot.Length:N0} bytes of JSON)");

        var preview = await us.Transfer.PreviewAsync(snapshot, cancellationToken);
        var reusedDefinitions = preview.IdentityMap.Count(static mapping =>
            mapping is { EntityKind: PortableEntityKind.DefinitionVersion, Reason: PortableIdentityMappingReason.ReusedIdentical });
        output.WriteLine($"    preview: can import = {preview.CanImport}, {preview.Counts.ResourceVersions} resource versions, {reusedDefinitions} identical definitions reused");
        output.WriteLine("    the eu prompt collides with the us one, so it is remapped and the agent's reference follows it:");
        foreach (var mapping in preview.IdentityMap.Where(static mapping => mapping is
        {
            Reason: PortableIdentityMappingReason.RemappedDivergent,
            EntityKind: PortableEntityKind.ResourceVersion or PortableEntityKind.ReferenceValue,
        }))
        {
            output.WriteLine($"      {mapping.EntityKind}: {mapping.SourceId} -> {mapping.TargetId}{(mapping.Location is null ? "" : $" at {mapping.Location}")}");
        }

        var imported = await us.Transfer.ImportAsync(snapshot, cancellationToken);
        output.WriteLine($"    import: {imported.Status}");
        var resolved = await us.Registry.ResolveAsync(AgentId, Channels.Production, "conversation-1", cancellationToken);
        output.WriteLine($"    us production resolves {Describe(resolved)}");
        var usOwn = await us.Registry.ResolvePromptAsync(PromptId, Channels.Production, "conversation-1", cancellationToken);
        output.WriteLine($"    the us team's own {PromptId} is untouched: \"{usOwn.Template}\"");
    }

    private static async Task SeedAsync(PromptRegistryService registry, CancellationToken cancellationToken)
    {
        await registry.SaveToolAsync(KnowledgeBaseToolId, new Tool { Name = "search_kb", Description = "Search the knowledge base." }, cancellationToken);
        await registry.SaveToolAsync(TicketToolId, new Tool { Name = "create_ticket", Description = "Open a support ticket." }, cancellationToken);
        await registry.SavePromptAsync(PromptId, TriageV1, cancellationToken);
        await registry.SaveAgentAsync(AgentId, SupportAgent, cancellationToken);
    }

    private static async Task DescribeAsync(RegistryEnvironment environment, TextWriter output, CancellationToken cancellationToken)
    {
        var history = environment.Services.GetRequiredService<IResourceVersionHistoryService>();
        var histories = await history.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
        {
            ResourceIds = [AgentId, PromptId, KnowledgeBaseToolId, TicketToolId],
        }, cancellationToken);

        foreach (var resource in histories.Histories)
        {
            var versions = resource.Versions.Select(static version =>
                $"v{version.Version} {(version.ActiveChannels.Count > 0 ? string.Join("+", version.ActiveChannels) : version.IsLatest ? "draft" : "inactive")}");
            output.WriteLine($"    {resource.ResourceId,-22} {string.Join(" | ", versions)}");
        }

        output.WriteLine();
    }

    private static string Describe(ResolvedAgent agent) =>
        $"{agent.AgentId} v{agent.AgentVersion} -> {agent.PromptId} v{agent.PromptVersion} " +
        $"({agent.Prompt.Model.Model}, temperature {agent.Prompt.Model.Temperature}), tools [{string.Join(", ", agent.Tools.Select(static tool => tool.Tool.Name))}]";
}
