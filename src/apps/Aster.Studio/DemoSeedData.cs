using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Studio;

internal static class DemoSeedData
{
    // Starting content for each tenant's shared aspect store. Seeded only when an aspect is missing, so edits made in
    // the Aspect Library survive restarts; Studio reads aspects from the store, never from this list.
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
        Seeder ForTenant(TenantScope tenant) => new(
            scope.ServiceProvider.GetRequiredService<IAspectDefinitionStore>(),
            scope.ServiceProvider.GetRequiredService<IResourceDefinitionStore>(),
            scope.ServiceProvider.GetRequiredService<IResourceManager>(),
            tenant,
            logger,
            cancellationToken);

        await ForTenant(TenantScope.Default).SeedAsync(SeedDefaultTenantAsync);
        await ForTenant(StudioTenants.Acme).SeedAsync(SeedAcmeTenantAsync);
    }

    // Fixed IDs in place of BuildAspect's random ones: every tenant registers identical aspect versions, so a snapshot
    // pinning them imports into another tenant by reusing that tenant's copy instead of colliding with it.
    private static AspectDefinition WithSeedIds(AspectDefinition aspect) => aspect with
    {
        Id = $"seed-{aspect.AspectDefinitionId}",
        FacetDefinitions = aspect.FacetDefinitions.Select(facet => facet with { Id = $"seed-{aspect.AspectDefinitionId}-{facet.FacetDefinitionId}" }).ToList(),
    };

    private static async Task SeedDefaultTenantAsync(Seeder seeder)
    {
        await seeder.RegisterDefinitionAsync("article", ["content", "seo", "publishable", "taggable", "navigation"]);
        await seeder.RegisterDefinitionAsync("page", ["content", "seo", "publishable", "navigation"]);

        var articleIntro = await seeder.CreateAsync("article", "article-aster-sdk-intro", new()
        {
            ["content"] = new() { ["title"] = "Introducing the Aster SDK", ["summary"] = "A quick tour of horizontal aspects.", ["body"] = "Aster models content as immutable resources composed from reusable aspects." },
            ["seo"] = new() { ["slug"] = "introducing-the-aster-sdk", ["metaTitle"] = "Introducing Aster", ["metaDescription"] = "A short introduction to the Aster SDK demo." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["taggable"] = new() { ["tags"] = new[] { "sdk", "demo", "aster" } },
        });
        articleIntro = await seeder.UpdateAsync(articleIntro.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Introducing the Aster SDK", ["summary"] = "A guided tour of horizontal aspects.", ["body"] = "Aster models content as immutable resources composed from reusable aspects." },
            ["seo"] = new() { ["slug"] = "introducing-the-aster-sdk", ["metaTitle"] = "Introducing Aster SDK", ["metaDescription"] = "Explore the Aster SDK demo and its reusable aspects." },
        });
        articleIntro = await seeder.UpdateAsync(articleIntro.ResourceId, 2, new()
        {
            ["content"] = new() { ["title"] = "Introducing the Aster SDK", ["summary"] = "A guided tour of definitions, resources, and activation.", ["body"] = "Aster models content as immutable resources composed from reusable aspects." },
        });
        await seeder.ActivateAsync(articleIntro.ResourceId, 2, "live");
        await seeder.ActivateAsync(articleIntro.ResourceId, 3, "preview");

        var articleAspects = await seeder.CreateAsync("article", "article-horizontal-aspects", new()
        {
            ["content"] = new() { ["title"] = "Composing Horizontal Aspects", ["summary"] = "Reuse content, SEO, publishing, and tagging aspects.", ["body"] = "Shared aspects let teams share fields across content types." },
            ["seo"] = new() { ["slug"] = "composing-horizontal-aspects", ["metaTitle"] = "Horizontal Aspects", ["metaDescription"] = "Compose content types from reusable shared aspects." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-02" },
            ["taggable"] = new() { ["tags"] = new[] { "aspects", "composition" } },
            ["navigation"] = new() { ["menuOrder"] = 30, ["showInMenu"] = true },
        });
        articleAspects = await seeder.UpdateAsync(articleAspects.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Composing Horizontal Aspects", ["summary"] = "Reuse content, SEO, publishing, and tagging fields.", ["body"] = "Shared aspects let teams share fields across content types." },
        });
        await seeder.ActivateAsync(articleAspects.ResourceId, 1, "live");
        await seeder.ActivateAsync(articleAspects.ResourceId, 2, "preview");

        var articleVersioning = await seeder.CreateAsync("article", "article-versioning", new()
        {
            ["content"] = new() { ["title"] = "Immutable Resource Versioning", ["summary"] = "Every edit creates a new resource version.", ["body"] = "Version history makes draft, preview, and live workflows explicit." },
            ["seo"] = new() { ["slug"] = "immutable-resource-versioning", ["metaTitle"] = "Immutable Versioning", ["metaDescription"] = "Understand Aster's immutable resource version model." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-03" },
            ["taggable"] = new() { ["tags"] = new[] { "versioning", "workflow" } },
        });
        await seeder.ActivateAsync(articleVersioning.ResourceId, 1, "preview");

        var articleSqlite = await seeder.CreateAsync("article", "article-sqlite-json", new()
        {
            ["content"] = new() { ["title"] = "SQLite JSON Persistence", ["summary"] = "Run the demo against a local SQLite database.", ["body"] = "The SQLite JSON provider stores definitions, versions, and activation state." },
            ["seo"] = new() { ["slug"] = "sqlite-json-persistence", ["metaTitle"] = "SQLite JSON", ["metaDescription"] = "Use the Aster SQLite JSON persistence provider." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-04" },
            ["taggable"] = new() { ["tags"] = new[] { "sqlite", "persistence" } },
        });
        await seeder.ActivateAsync(articleSqlite.ResourceId, 1, "live");

        // Never activated, so the archive-drafts policy has a candidate from the start.
        await seeder.CreateAsync("article", "article-roadmap-draft", new()
        {
            ["content"] = new() { ["title"] = "Roadmap Notes", ["summary"] = "An unpublished draft.", ["body"] = "Ideas that never made it to preview." },
            ["seo"] = new() { ["slug"] = "roadmap-notes" },
            ["taggable"] = new() { ["tags"] = new[] { "roadmap" } },
        });

        var home = await seeder.CreateAsync("page", "page-home", new()
        {
            ["content"] = new() { ["title"] = "Home", ["summary"] = "Studio landing page.", ["body"] = "Welcome to the Aster Studio." },
            ["seo"] = new() { ["slug"] = "home", ["metaTitle"] = "Aster Studio", ["metaDescription"] = "Demo admin experience for the Aster SDK." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["navigation"] = new() { ["menuOrder"] = 0, ["showInMenu"] = true },
        });
        home = await seeder.UpdateAsync(home.ResourceId, 1, new()
        {
            ["content"] = new() { ["title"] = "Home", ["summary"] = "Aster Studio landing page.", ["body"] = "Welcome to the Aster Studio backend and SDK showcase." },
        });
        await seeder.ActivateAsync(home.ResourceId, 2, "live");
        await seeder.ActivateAsync(home.ResourceId, 2, "preview");

        var about = await seeder.CreateAsync("page", "page-about", new()
        {
            ["content"] = new() { ["title"] = "About", ["summary"] = "About this SDK demo.", ["body"] = "This page explains the backend resources used by the demo." },
            ["seo"] = new() { ["slug"] = "about", ["metaTitle"] = "About Aster", ["metaDescription"] = "Learn about the Aster SDK demo." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-01" },
            ["navigation"] = new() { ["menuOrder"] = 10, ["showInMenu"] = true },
        });
        await seeder.ActivateAsync(about.ResourceId, 1, "live");

        var contact = await seeder.CreateAsync("page", "page-contact", new()
        {
            ["content"] = new() { ["title"] = "Contact", ["summary"] = "Contact page draft.", ["body"] = "Reach the demo team through your host application." },
            ["seo"] = new() { ["slug"] = "contact", ["metaTitle"] = "Contact", ["metaDescription"] = "Contact page for the demo." },
            ["publishable"] = new() { ["publishFrom"] = "2026-07-05" },
            ["navigation"] = new() { ["menuOrder"] = 20, ["showInMenu"] = false },
        });
        contact = await seeder.UpdateAsync(contact.ResourceId, 1, new()
        {
            ["navigation"] = new() { ["menuOrder"] = 20, ["showInMenu"] = true },
        });
        await seeder.ActivateAsync(contact.ResourceId, 2, "preview");

        // Article v2 drops navigation and declares policies after the articles exist, so they show a schema upgrade
        // that carries navigation forward where an article has it.
        await seeder.RegisterDefinitionAsync("article", ["content", "seo", "publishable", "taggable"], ArticlePolicies);
    }

    // A second tenant. Its page content type and page-home share IDs with the default tenant's but not their
    // content, so importing those from the default tenant remaps them.
    private static async Task SeedAcmeTenantAsync(Seeder seeder)
    {
        await seeder.RegisterDefinitionAsync("page", ["content", "seo"]);
        await seeder.RegisterDefinitionAsync("product", ["content", "taggable"]);

        await seeder.CreateAsync("page", "page-home", new()
        {
            ["content"] = new() { ["title"] = "Acme Home", ["summary"] = "Everything Acme makes.", ["body"] = "Welcome to the Acme catalog." },
            ["seo"] = new() { ["slug"] = "home", ["metaTitle"] = "Acme" },
        });
        await seeder.ActivateAsync("page-home", 1, "live");

        await seeder.CreateAsync("page", "page-careers", new()
        {
            ["content"] = new() { ["title"] = "Careers at Acme", ["summary"] = "Join the workshop." },
            ["seo"] = new() { ["slug"] = "careers" },
        });

        await seeder.CreateAsync("product", "product-anvil", new()
        {
            ["content"] = new() { ["title"] = "Anvil", ["summary"] = "Our best seller since 1949." },
            ["taggable"] = new() { ["tags"] = new[] { "hardware", "classic" } },
        });
        await seeder.ActivateAsync("product-anvil", 1, "live");

        await seeder.CreateAsync("product", "product-rocket-skates", new()
        {
            ["content"] = new() { ["title"] = "Rocket Skates", ["summary"] = "Fast, mostly." },
            ["taggable"] = new() { ["tags"] = new[] { "hardware", "speed" } },
        });
    }

    /// <summary>Seeds one tenant: its shared aspects when missing, and its content when it has no content types yet.</summary>
    private sealed class Seeder(
        IAspectDefinitionStore aspectStore,
        IResourceDefinitionStore definitionStore,
        IResourceManager resourceManager,
        TenantScope tenant,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        public async Task SeedAsync(Func<Seeder, Task> seedContent)
        {
            foreach (var aspect in SeedAspects)
            {
                if (await aspectStore.GetLatestAsync(aspect.AspectDefinitionId, tenant, cancellationToken) is not null)
                    continue;

                await aspectStore.RegisterAsync(WithSeedIds(aspect), tenant, cancellationToken);
                logger.LogInformation("Seeded shared aspect '{AspectId}' in tenant '{TenantId}'.", aspect.AspectDefinitionId, tenant.TenantId);
            }

            if ((await definitionStore.ListDefinitionsAsync(tenant, cancellationToken)).Any())
                return;

            logger.LogInformation("Seeding content in tenant '{TenantId}'.", tenant.TenantId);
            await seedContent(this);
        }

        public async Task RegisterDefinitionAsync(string definitionId, IReadOnlyList<string> aspectIds, IReadOnlyList<ResourcePolicyDeclaration>? policies = null)
        {
            var builder = new ResourceDefinitionBuilder().WithDefinitionId(definitionId);
            foreach (var aspectId in aspectIds)
                builder.WithSharedAspect(aspectId);
            foreach (var policy in policies ?? [])
                builder.WithPolicy(policy);

            await definitionStore.RegisterDefinitionAsync(await builder.BuildAsync(aspectStore, tenant, cancellationToken), tenant, cancellationToken);
        }

        public async Task<Resource> CreateAsync(string definitionId, string resourceId, Dictionary<string, Dictionary<string, object?>> aspects) =>
            await resourceManager.CreateAsync(definitionId, new CreateResourceRequest
            {
                TenantScope = tenant,
                ResourceId = resourceId,
                InitialAspects = DemoJsonValues.ToSdkAspects(aspects),
            }, cancellationToken);

        public async Task<Resource> UpdateAsync(string resourceId, int baseVersion, Dictionary<string, Dictionary<string, object?>> aspects) =>
            await resourceManager.UpdateAsync(resourceId, new UpdateResourceRequest
            {
                TenantScope = tenant,
                BaseVersion = baseVersion,
                AspectUpdates = DemoJsonValues.ToSdkAspects(aspects),
            }, cancellationToken);

        public async Task ActivateAsync(string resourceId, int version, string channel) =>
            await resourceManager.ActivateAsync(resourceId, version, channel, tenant, allowMultipleActive: false, cancellationToken);
    }
}
