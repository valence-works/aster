using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Tests.Persistence;

/// <summary>
/// Tests for query filter operators (<c>Equals</c>, <c>Contains</c>, <c>Range</c>)
/// applied to resource metadata and aspect values via the Sqlite provider.
/// Covers: MetadataFilter, AspectPresenceFilter, FacetValueFilter, LogicalExpression.
/// </summary>
public sealed class SqliteQueryOperatorTests : IDisposable
{
    // ── POCOs ────────────────────────────────────────────────────────────────

    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount, string Currency);
    private sealed record TagsAspect(string Category, string Colour);

    // ── Fixture ──────────────────────────────────────────────────────────────

    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    // ── Seed helper ──────────────────────────────────────────────────────────

    private async Task<List<Resource>> SeedResources()
    {
        var resources = new List<Resource>
        {
            new()
            {
                ResourceId = "res-alpha",
                Id = "v-alpha-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = "alice",
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Super Gadget") },
                    { "PriceAspect", new PriceAspect(49.99m, "USD") },
                    { "TagsAspect", new TagsAspect("Electronics", "Red") },
                },
            },
            new()
            {
                ResourceId = "res-beta",
                Id = "v-beta-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = "bob",
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Budget Widget") },
                    { "PriceAspect", new PriceAspect(9.99m, "EUR") },
                },
            },
            new()
            {
                ResourceId = "res-gamma",
                Id = "v-gamma-1",
                DefinitionId = "Article",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = "alice",
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Aster Deep Dive") },
                },
            },
            new()
            {
                ResourceId = "res-delta",
                Id = "v-delta-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = null,
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Mystery Item") },
                },
            },
        };

        foreach (var r in resources)
            await fixture.WriteStore.SaveVersionAsync(r);

        return resources;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MetadataFilter — Equals
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Equals_DefinitionId_FiltersCorrectly()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
        })).ToList();

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("Product", r.DefinitionId));
    }

    [Fact]
    public async Task Equals_Owner_FiltersCorrectly()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
        })).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("alice", r.Owner));
    }

    [Fact]
    public async Task Equals_ResourceId_ReturnsSingleMatch()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("ResourceId", "res-beta", ComparisonOperator.Equals),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("res-beta", results[0].ResourceId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MetadataFilter — Contains
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Contains_Owner_MatchesSubstring()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("Owner", "lic", ComparisonOperator.Contains),
        })).ToList();

        // "alice" contains "lic"
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("alice", r.Owner));
    }

    [Fact]
    public async Task Contains_ResourceId_MatchesSubstring()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("ResourceId", "res-", ComparisonOperator.Contains),
        })).ToList();

        // All resource IDs start with "res-"
        Assert.Equal(4, results.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  AspectPresenceFilter
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AspectPresence_ReturnsOnlyResourcesWithAspect()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new AspectPresenceFilter("PriceAspect"),
        })).ToList();

        // Only res-alpha and res-beta have PriceAspect
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.ResourceId == "res-alpha");
        Assert.Contains(results, r => r.ResourceId == "res-beta");
    }

    [Fact]
    public async Task AspectPresence_NoMatch_ReturnsEmpty()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new AspectPresenceFilter("NonExistentAspect"),
        })).ToList();

        Assert.Empty(results);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FacetValueFilter — Equals
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FacetValue_Equals_MatchesFacetInAspect()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("PriceAspect", "currency", "USD", ComparisonOperator.Equals),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("res-alpha", results[0].ResourceId);
    }

    [Fact]
    public async Task FacetValue_Equals_NoMatch_ReturnsEmpty()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("PriceAspect", "currency", "GBP", ComparisonOperator.Equals),
        })).ToList();

        Assert.Empty(results);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FacetValueFilter — Contains
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FacetValue_Contains_MatchesSubstring()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("TitleAspect", "title", "Gadget", ComparisonOperator.Contains),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("res-alpha", results[0].ResourceId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  LogicalExpression — AND, OR, NOT
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Logical_And_IntersectsFilters()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.And,
            [
                new MetadataFilter("DefinitionId", "Product", ComparisonOperator.Equals),
                new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
            ]),
        })).ToList();

        // Only res-alpha is Product AND owned by alice
        Assert.Single(results);
        Assert.Equal("res-alpha", results[0].ResourceId);
    }

    [Fact]
    public async Task Logical_Or_UnionsFilters()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.Or,
            [
                new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
                new MetadataFilter("Owner", "bob", ComparisonOperator.Equals),
            ]),
        })).ToList();

        // alice (2 resources) + bob (1 resource) = 3
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Logical_Not_InvertsFilter()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.Not,
            [
                new MetadataFilter("DefinitionId", "Product", ComparisonOperator.Equals),
            ]),
        })).ToList();

        // Only res-gamma is NOT a Product
        Assert.Single(results);
        Assert.Equal("res-gamma", results[0].ResourceId);
    }

    [Fact]
    public async Task Logical_NestedAndOr_CombinesCorrectly()
    {
        await SeedResources();

        // (DefinitionId = Product) AND (Owner = alice OR Owner = bob)
        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.And,
            [
                new MetadataFilter("DefinitionId", "Product", ComparisonOperator.Equals),
                new LogicalExpression(LogicalOperator.Or,
                [
                    new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
                    new MetadataFilter("Owner", "bob", ComparisonOperator.Equals),
                ]),
            ]),
        })).ToList();

        // res-alpha (product, alice) + res-beta (product, bob)
        Assert.Equal(2, results.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DefinitionId shortcut + Filter combined
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DefinitionIdShortcut_PlusFilter_CombinesAsAnd()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = new MetadataFilter("Owner", "bob", ComparisonOperator.Equals),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("res-beta", results[0].ResourceId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Latest version only (query operates on latest versions)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_ReturnsOnlyLatestVersion()
    {
        // V1 and V2 of the same resource
        var v1 = new Resource
        {
            ResourceId = "res-multi",
            Id = "v-multi-1",
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
            Owner = "alice",
            Aspects = new Dictionary<string, object>
            {
                { "TitleAspect", new TitleAspect("Version One") },
            },
        };
        var v2 = new Resource
        {
            ResourceId = "res-multi",
            Id = "v-multi-2",
            DefinitionId = "Product",
            Version = 2,
            Created = DateTime.UtcNow,
            Owner = "alice",
            Aspects = new Dictionary<string, object>
            {
                { "TitleAspect", new TitleAspect("Version Two") },
            },
        };

        await fixture.WriteStore.SaveVersionAsync(v1);
        await fixture.WriteStore.SaveVersionAsync(v2);

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
        })).ToList();

        Assert.Single(results);
        Assert.Equal(2, results[0].Version);
        Assert.Equal("res-multi", results[0].ResourceId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Empty result set
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoMatchingResources_ReturnsEmptyList()
    {
        await SeedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "NonExistent",
        })).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public async Task NoResources_ReturnsEmptyList()
    {
        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery())).ToList();
        Assert.Empty(results);
    }
}
