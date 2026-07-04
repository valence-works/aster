using Aster.Core.Abstractions;

namespace Aster.AdminDemo;

internal static class DemoSeedData
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var definitionStore = scope.ServiceProvider.GetRequiredService<IResourceDefinitionStore>();
        var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();

        if ((await definitionStore.ListDefinitionsAsync(cancellationToken)).Any())
            return;

        logger.LogInformation("Seeding Aster AdminDemo SQLite database.");
        await SeedDefinitionsAsync(definitionStore, cancellationToken);
        await SeedResourcesAsync(resourceManager, cancellationToken);
    }

    private static async Task SeedDefinitionsAsync(IResourceDefinitionStore definitionStore, CancellationToken cancellationToken)
    {
        await definitionStore.RegisterDefinitionAsync(BuildFromTemplates("article", false, ["content", "seo", "publishable", "taggable"]), cancellationToken);
        await definitionStore.RegisterDefinitionAsync(BuildFromTemplates("page", false, ["content", "seo", "publishable", "navigation"]), cancellationToken);
    }

    private static async Task SeedResourcesAsync(IResourceManager resourceManager, CancellationToken cancellationToken)
    {
        var articleIntro = await CreateAsync(resourceManager, "article", "article-aster-sdk-intro", new()
        {
            ["content"] = new() { ["title"] = "Introducing the Aster SDK", ["summary"] = "A quick tour of horizontal aspects.", ["body"] = "Aster models content as immutable resources composed from reusable aspects." },
            ["seo"] = new() { ["slug"] = "introducing-the-aster-sdk", ["metaTitle"] = "Introducing Aster", ["metaDescription"] = "A short introduction to the Aster SDK demo." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["taggable"] = new() { ["tags"] = "sdk,demo,aster" },
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
            ["content"] = new() { ["title"] = "Composing Horizontal Aspects", ["summary"] = "Reuse content, SEO, publishing, and tagging aspects.", ["body"] = "Aspect templates let teams share fields across content types." },
            ["seo"] = new() { ["slug"] = "composing-horizontal-aspects", ["metaTitle"] = "Horizontal Aspects", ["metaDescription"] = "Compose content types from reusable aspect templates." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-02" },
            ["taggable"] = new() { ["tags"] = "aspects,composition" },
        }, cancellationToken);
        articleAspects = await UpdateAsync(resourceManager, articleAspects.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Composing Horizontal Aspects", ["summary"] = "Reuse content, SEO, publishing, and tagging fields.", ["body"] = "Aspect templates let teams share fields across content types." },
        }, cancellationToken);
        await resourceManager.ActivateAsync(articleAspects.ResourceId, 1, "live", cancellationToken: cancellationToken);
        await resourceManager.ActivateAsync(articleAspects.ResourceId, 2, "preview", cancellationToken: cancellationToken);

        var articleVersioning = await CreateAsync(resourceManager, "article", "article-versioning", new()
        {
            ["content"] = new() { ["title"] = "Immutable Resource Versioning", ["summary"] = "Every edit creates a new resource version.", ["body"] = "Version history makes draft, preview, and live workflows explicit." },
            ["seo"] = new() { ["slug"] = "immutable-resource-versioning", ["metaTitle"] = "Immutable Versioning", ["metaDescription"] = "Understand Aster's immutable resource version model." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-03" },
            ["taggable"] = new() { ["tags"] = "versioning,workflow" },
        }, cancellationToken);
        await resourceManager.ActivateAsync(articleVersioning.ResourceId, 1, "preview", cancellationToken: cancellationToken);

        var articleSqlite = await CreateAsync(resourceManager, "article", "article-sqlite-json", new()
        {
            ["content"] = new() { ["title"] = "SQLite JSON Persistence", ["summary"] = "Run the demo against a local SQLite database.", ["body"] = "The SQLite JSON provider stores definitions, versions, and activation state." },
            ["seo"] = new() { ["slug"] = "sqlite-json-persistence", ["metaTitle"] = "SQLite JSON", ["metaDescription"] = "Use the Aster SQLite JSON persistence provider." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-04" },
            ["taggable"] = new() { ["tags"] = "sqlite,persistence" },
        }, cancellationToken);
        await resourceManager.ActivateAsync(articleSqlite.ResourceId, 1, "live", cancellationToken: cancellationToken);

        var home = await CreateAsync(resourceManager, "page", "page-home", new()
        {
            ["content"] = new() { ["title"] = "Home", ["summary"] = "Admin demo landing page.", ["body"] = "Welcome to the Aster AdminDemo." },
            ["seo"] = new() { ["slug"] = "home", ["metaTitle"] = "Aster AdminDemo", ["metaDescription"] = "Demo admin experience for the Aster SDK." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["navigation"] = new() { ["menuOrder"] = 0, ["showInMenu"] = true },
        }, cancellationToken);
        home = await UpdateAsync(resourceManager, home.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Home", ["summary"] = "Aster AdminDemo landing page.", ["body"] = "Welcome to the Aster AdminDemo backend and SDK showcase." },
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

    private static Aster.Core.Models.Definitions.ResourceDefinition BuildFromTemplates(string definitionId, bool isSingleton, IReadOnlyList<string> templateKeys) =>
        DemoDefinitions.BuildDefinition(
            definitionId,
            isSingleton,
            templateKeys.Select(key =>
            {
                var template = AspectTemplateCatalog.Find(key) ?? throw new InvalidOperationException($"Template '{key}' was not found.");
                return (template.Key, template.Facets);
            }));

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
