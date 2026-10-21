using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;

namespace Aster.Studio;

internal static class DemoSeedData
{
    // Starting content for the shared aspect store. Seeded only when an aspect is missing, so edits made in the
    // Aspect Library survive restarts; Studio reads aspects from the store, never from this list.
    private static readonly IReadOnlyList<AspectDefinition> SeedAspects =
    [
        DemoDefinitions.BuildAspect("content", false,
        [
            new("title", "string", true, new FacetMetadataDto { Label = "Title", UiHint = UiHints.Text, MaxLength = 200, DisplayOrder = 1 }),
            new("summary", "string", false, new FacetMetadataDto { UiHint = UiHints.Multiline, MaxLength = 500, DisplayOrder = 2 }),
            new("body", "string", false, new FacetMetadataDto { UiHint = UiHints.Markdown, DisplayOrder = 3 }),
        ]),
        DemoDefinitions.BuildAspect("seo", false,
        [
            new("slug", "string", true, new FacetMetadataDto { UiHint = UiHints.Slug, Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$", DisplayOrder = 1 }),
            new("metaTitle", "string", false, new FacetMetadataDto { MaxLength = 60, DisplayOrder = 2 }),
            new("metaDescription", "string", false, new FacetMetadataDto { UiHint = UiHints.Multiline, MaxLength = 160, DisplayOrder = 3 }),
        ]),
        DemoDefinitions.BuildAspect("publishable", false,
        [
            new("publishFrom", "datetime", false, new FacetMetadataDto { UiHint = UiHints.Date, DisplayOrder = 1 }),
            new("publishUntil", "datetime", false, new FacetMetadataDto { UiHint = UiHints.Date, DisplayOrder = 2 }),
        ]),
        DemoDefinitions.BuildAspect("taggable", false,
        [
            new("tags", "string[]", false, new FacetMetadataDto { UiHint = UiHints.Tags, Description = "Up to 10 distinct tags", MaxLength = 40, MaxItems = 10, UniqueItems = true, DisplayOrder = 1 }),
        ]),
        DemoDefinitions.BuildAspect("navigation", false,
        [
            new("menuOrder", "number", false, new FacetMetadataDto { MinValue = 0, MaxValue = 100, DefaultValue = 0m, DisplayOrder = 1 }),
            new("showInMenu", "boolean", false, new FacetMetadataDto { UiHint = UiHints.Switch, DefaultValue = false, DisplayOrder = 2 }),
        ]),
    ];

    // Declared on article v2, so the policy screens have archive, soft-delete and pruning outcomes to preview.
    private static readonly IReadOnlyList<ResourcePolicyDeclaration> ArticlePolicies =
    [
        new()
        {
            PolicyId = "archive-drafts",
            Name = "Archive articles whose latest version was never activated",
            Kind = ResourcePolicyKind.Archival,
            Target = ResourcePolicyTarget.Resource,
            Outcome = ResourcePolicyOutcome.Archive,
            Criteria = new() { ActivationState = ResourcePolicyActivationState.Draft, LifecycleState = ResourceLifecycleMarkerState.None },
        },
        new()
        {
            PolicyId = "soft-delete-stale-drafts",
            Name = "Soft-delete drafts older than a year",
            Kind = ResourcePolicyKind.SoftDelete,
            Target = ResourcePolicyTarget.Resource,
            Outcome = ResourcePolicyOutcome.SoftDelete,
            Criteria = new() { ActivationState = ResourcePolicyActivationState.Draft, LifecycleState = ResourceLifecycleMarkerState.None, MinimumAge = TimeSpan.FromDays(365) },
        },
        new()
        {
            PolicyId = "keep-latest-version",
            Name = "Keep only the latest version",
            Kind = ResourcePolicyKind.VersionPruning,
            Target = ResourcePolicyTarget.ResourceVersion,
            Outcome = ResourcePolicyOutcome.PrunePreview,
            Criteria = new() { MaximumRetainedVersions = 1 },
        },
    ];

    public static async Task InitializeAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var aspectStore = scope.ServiceProvider.GetRequiredService<IAspectDefinitionStore>();
        var definitionStore = scope.ServiceProvider.GetRequiredService<IResourceDefinitionStore>();
        var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();

        await SeedAspectsAsync(aspectStore, logger, cancellationToken);

        if ((await definitionStore.ListDefinitionsAsync(cancellationToken)).Any())
            return;

        logger.LogInformation("Seeding Aster Studio SQLite database.");
        await SeedDefinitionsAsync(definitionStore, aspectStore, cancellationToken);
        await SeedResourcesAsync(resourceManager, cancellationToken);

        // Article v2 drops navigation and declares policies after the articles exist, so they show a schema upgrade
        // that carries navigation forward where an article has it.
        await definitionStore.RegisterDefinitionAsync(
            await BuildDefinitionAsync(aspectStore, "article", ["content", "seo", "publishable", "taggable"], ArticlePolicies, cancellationToken),
            cancellationToken);
    }

    private static async Task SeedAspectsAsync(IAspectDefinitionStore aspectStore, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var aspect in SeedAspects)
        {
            if (await aspectStore.GetLatestAsync(aspect.AspectDefinitionId, cancellationToken: cancellationToken) is not null)
                continue;

            await aspectStore.RegisterAsync(aspect, cancellationToken: cancellationToken);
            logger.LogInformation("Seeded shared aspect '{AspectId}'.", aspect.AspectDefinitionId);
        }
    }

    private static async Task SeedDefinitionsAsync(
        IResourceDefinitionStore definitionStore,
        IAspectDefinitionStore aspectStore,
        CancellationToken cancellationToken)
    {
        await definitionStore.RegisterDefinitionAsync(
            await BuildDefinitionAsync(aspectStore, "article", ["content", "seo", "publishable", "taggable", "navigation"], [], cancellationToken),
            cancellationToken);
        await definitionStore.RegisterDefinitionAsync(
            await BuildDefinitionAsync(aspectStore, "page", ["content", "seo", "publishable", "navigation"], [], cancellationToken),
            cancellationToken);
    }

    private static async Task<ResourceDefinition> BuildDefinitionAsync(
        IAspectDefinitionStore aspectStore,
        string definitionId,
        IReadOnlyList<string> aspectIds,
        IReadOnlyList<ResourcePolicyDeclaration> policies,
        CancellationToken cancellationToken)
    {
        var builder = new ResourceDefinitionBuilder().WithDefinitionId(definitionId);
        foreach (var aspectId in aspectIds)
            builder.WithSharedAspect(aspectId);
        foreach (var policy in policies)
            builder.WithPolicy(policy);

        return await builder.BuildAsync(aspectStore, cancellationToken: cancellationToken);
    }

    private static async Task SeedResourcesAsync(IResourceManager resourceManager, CancellationToken cancellationToken)
    {
        var articleIntro = await CreateAsync(resourceManager, "article", "article-aster-sdk-intro", new()
        {
            ["content"] = new() { ["title"] = "Introducing the Aster SDK", ["summary"] = "A quick tour of horizontal aspects.", ["body"] = "Aster models content as immutable resources composed from reusable aspects." },
            ["seo"] = new() { ["slug"] = "introducing-the-aster-sdk", ["metaTitle"] = "Introducing Aster", ["metaDescription"] = "A short introduction to the Aster SDK demo." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["taggable"] = new() { ["tags"] = new[] { "sdk", "demo", "aster" } },
        }, cancellationToken);
        articleIntro = await UpdateAsync(resourceManager, articleIntro.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Introducing the Aster SDK", ["summary"] = "A guided tour of horizontal aspects.", ["body"] = "Aster models content as immutable resources composed from reusable aspects." },
            ["seo"] = new() { ["slug"] = "introducing-the-aster-sdk", ["metaTitle"] = "Introducing Aster SDK", ["metaDescription"] = "Explore the Aster SDK demo and its reusable aspects." },
        }, cancellationToken);
        articleIntro = await UpdateAsync(resourceManager, articleIntro.ResourceId, 2, new()
        {
            ["content"] = new() { ["title"] = "Introducing the Aster SDK", ["summary"] = "A guided tour of definitions, resources, and activation.", ["body"] = "Aster models content as immutable resources composed from reusable aspects." },
        }, cancellationToken);
        await resourceManager.ActivateAsync(articleIntro.ResourceId, 2, "live", cancellationToken: cancellationToken);
        await resourceManager.ActivateAsync(articleIntro.ResourceId, 3, "preview", cancellationToken: cancellationToken);

        var articleAspects = await CreateAsync(resourceManager, "article", "article-horizontal-aspects", new()
        {
            ["content"] = new() { ["title"] = "Composing Horizontal Aspects", ["summary"] = "Reuse content, SEO, publishing, and tagging aspects.", ["body"] = "Shared aspects let teams share fields across content types." },
            ["seo"] = new() { ["slug"] = "composing-horizontal-aspects", ["metaTitle"] = "Horizontal Aspects", ["metaDescription"] = "Compose content types from reusable shared aspects." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-02" },
            ["taggable"] = new() { ["tags"] = new[] { "aspects", "composition" } },
            ["navigation"] = new() { ["menuOrder"] = 30, ["showInMenu"] = true },
        }, cancellationToken);
        articleAspects = await UpdateAsync(resourceManager, articleAspects.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Composing Horizontal Aspects", ["summary"] = "Reuse content, SEO, publishing, and tagging fields.", ["body"] = "Shared aspects let teams share fields across content types." },
        }, cancellationToken);
        await resourceManager.ActivateAsync(articleAspects.ResourceId, 1, "live", cancellationToken: cancellationToken);
        await resourceManager.ActivateAsync(articleAspects.ResourceId, 2, "preview", cancellationToken: cancellationToken);

        var articleVersioning = await CreateAsync(resourceManager, "article", "article-versioning", new()
        {
            ["content"] = new() { ["title"] = "Immutable Resource Versioning", ["summary"] = "Every edit creates a new resource version.", ["body"] = "Version history makes draft, preview, and live workflows explicit." },
            ["seo"] = new() { ["slug"] = "immutable-resource-versioning", ["metaTitle"] = "Immutable Versioning", ["metaDescription"] = "Understand Aster's immutable resource version model." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-03" },
            ["taggable"] = new() { ["tags"] = new[] { "versioning", "workflow" } },
        }, cancellationToken);
        await resourceManager.ActivateAsync(articleVersioning.ResourceId, 1, "preview", cancellationToken: cancellationToken);

        var articleSqlite = await CreateAsync(resourceManager, "article", "article-sqlite-json", new()
        {
            ["content"] = new() { ["title"] = "SQLite JSON Persistence", ["summary"] = "Run the demo against a local SQLite database.", ["body"] = "The SQLite JSON provider stores definitions, versions, and activation state." },
            ["seo"] = new() { ["slug"] = "sqlite-json-persistence", ["metaTitle"] = "SQLite JSON", ["metaDescription"] = "Use the Aster SQLite JSON persistence provider." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-04" },
            ["taggable"] = new() { ["tags"] = new[] { "sqlite", "persistence" } },
        }, cancellationToken);
        await resourceManager.ActivateAsync(articleSqlite.ResourceId, 1, "live", cancellationToken: cancellationToken);

        // Never activated, so the archive-drafts policy has a candidate from the start.
        await CreateAsync(resourceManager, "article", "article-roadmap-draft", new()
        {
            ["content"] = new() { ["title"] = "Roadmap Notes", ["summary"] = "An unpublished draft.", ["body"] = "Ideas that never made it to preview." },
            ["seo"] = new() { ["slug"] = "roadmap-notes" },
            ["taggable"] = new() { ["tags"] = new[] { "roadmap" } },
        }, cancellationToken);

        var home = await CreateAsync(resourceManager, "page", "page-home", new()
        {
            ["content"] = new() { ["title"] = "Home", ["summary"] = "Studio landing page.", ["body"] = "Welcome to the Aster Studio." },
            ["seo"] = new() { ["slug"] = "home", ["metaTitle"] = "Aster Studio", ["metaDescription"] = "Demo admin experience for the Aster SDK." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["navigation"] = new() { ["menuOrder"] = 0, ["showInMenu"] = true },
        }, cancellationToken);
        home = await UpdateAsync(resourceManager, home.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Home", ["summary"] = "Aster Studio landing page.", ["body"] = "Welcome to the Aster Studio backend and SDK showcase." },
        }, cancellationToken);
        await resourceManager.ActivateAsync(home.ResourceId, 2, "live", cancellationToken: cancellationToken);
        await resourceManager.ActivateAsync(home.ResourceId, 2, "preview", cancellationToken: cancellationToken);

        var about = await CreateAsync(resourceManager, "page", "page-about", new()
        {
            ["content"] = new() { ["title"] = "About", ["summary"] = "About this SDK demo.", ["body"] = "This page explains the backend resources used by the demo." },
            ["seo"] = new() { ["slug"] = "about", ["metaTitle"] = "About Aster", ["metaDescription"] = "Learn about the Aster SDK demo." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["navigation"] = new() { ["menuOrder"] = 10, ["showInMenu"] = true },
        }, cancellationToken);
        await resourceManager.ActivateAsync(about.ResourceId, 1, "live", cancellationToken: cancellationToken);

        var contact = await CreateAsync(resourceManager, "page", "page-contact", new()
        {
            ["content"] = new() { ["title"] = "Contact", ["summary"] = "Contact page draft.", ["body"] = "Reach the demo team through your host application." },
            ["seo"] = new() { ["slug"] = "contact", ["metaTitle"] = "Contact", ["metaDescription"] = "Contact page for the demo." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-05" },
            ["navigation"] = new() { ["menuOrder"] = 20, ["showInMenu"] = false },
        }, cancellationToken);
        contact = await UpdateAsync(resourceManager, contact.ResourceId, 1, new()
        {
            ["navigation"] = new() { ["menuOrder"] = 20, ["showInMenu"] = true },
        }, cancellationToken);
        await resourceManager.ActivateAsync(contact.ResourceId, 2, "preview", cancellationToken: cancellationToken);
    }

    private static async Task<Aster.Core.Models.Instances.Resource> CreateAsync(
        IResourceManager resourceManager,
        string definitionId,
        string resourceId,
        Dictionary<string, Dictionary<string, object?>> aspects,
        CancellationToken cancellationToken) =>
        await resourceManager.CreateAsync(definitionId, new Aster.Core.Abstractions.CreateResourceRequest
        {
            ResourceId = resourceId,
            InitialAspects = DemoJsonValues.ToSdkAspects(aspects),
        }, cancellationToken);

    private static async Task<Aster.Core.Models.Instances.Resource> UpdateAsync(
        IResourceManager resourceManager,
        string resourceId,
        int baseVersion,
        Dictionary<string, Dictionary<string, object?>> aspects,
        CancellationToken cancellationToken) =>
        await resourceManager.UpdateAsync(resourceId, new Aster.Core.Abstractions.UpdateResourceRequest
        {
            BaseVersion = baseVersion,
            AspectUpdates = DemoJsonValues.ToSdkAspects(aspects),
        }, cancellationToken);
}
