using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Tests.Persistence;

/// <summary>
/// Tests that resources with missing (null) sortable field values are included
/// in query results and appear in a deterministic position. Per spec FR-011,
/// records missing sort field values remain in the result set with missing
/// values ordered last when a sort-related filter is NOT applied.
/// </summary>
public sealed class SqliteQueryNullSortTests : IDisposable
{
    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount, string Currency);

    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    /// <summary>
    /// Seeds resources where some have null Owner and some have null optional fields.
    /// </summary>
    private async Task SeedMixedResources()
    {
        var resources = new List<Resource>
        {
            new()
            {
                ResourceId = "res-001",
                Id = "v-001-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = "alice",
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Widget A") },
                    { "PriceAspect", new PriceAspect(10.0m, "USD") },
                },
            },
            new()
            {
                ResourceId = "res-002",
                Id = "v-002-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = null, // missing owner
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Widget B") },
                },
            },
            new()
            {
                ResourceId = "res-003",
                Id = "v-003-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = "bob",
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Widget C") },
                    { "PriceAspect", new PriceAspect(30.0m, "EUR") },
                },
            },
            new()
            {
                ResourceId = "res-004",
                Id = "v-004-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = null, // missing owner
                Hash = null,  // missing hash
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Widget D") },
                },
            },
            new()
            {
                ResourceId = "res-005",
                Id = "v-005-1",
                DefinitionId = "Article",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = "charlie",
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect("Article One") },
                },
            },
        };

        foreach (var r in resources)
            await fixture.WriteStore.SaveVersionAsync(r);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Null owner resources are included in unfiltered results
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnfilteredQuery_IncludesNullOwnerResources()
    {
        await SeedMixedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery())).ToList();

        // All 5 resources should appear
        Assert.Equal(5, results.Count);
        Assert.Contains(results, r => r.ResourceId == "res-002" && r.Owner is null);
        Assert.Contains(results, r => r.ResourceId == "res-004" && r.Owner is null);
    }

    [Fact]
    public async Task DefinitionIdFilter_IncludesNullOwnerResources()
    {
        await SeedMixedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
        })).ToList();

        // 4 Product resources (including 2 with null Owner)
        Assert.Equal(4, results.Count);
        Assert.Contains(results, r => r.Owner is null);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Resources with missing aspects appear in general queries
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingAspect_ResourceStillIncludedInUnfilteredQuery()
    {
        await SeedMixedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
        })).ToList();

        // res-002 and res-004 lack PriceAspect but should still be in Product results
        Assert.Equal(4, results.Count);
    }

    [Fact]
    public async Task MissingAspect_ExcludedByPresenceFilter()
    {
        await SeedMixedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new AspectPresenceFilter("PriceAspect"),
        })).ToList();

        // Only res-001 and res-003 have PriceAspect
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("PriceAspect", r.Aspects.Keys));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Deterministic order includes null-owner records
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SortOrder_DeterministicWithNullOwners()
    {
        await SeedMixedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
        })).ToList();

        // Sorted by ResourceId ASC: res-001, res-002, res-003, res-004
        Assert.Equal("res-001", results[0].ResourceId);
        Assert.Equal("res-002", results[1].ResourceId);
        Assert.Equal("res-003", results[2].ResourceId);
        Assert.Equal("res-004", results[3].ResourceId);
    }

    [Fact]
    public async Task Paging_NullOwnerRecords_NotSkippedInPages()
    {
        await SeedMixedResources();

        // Page through Products (4 total), page size 2
        var page1 = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Skip = 0,
            Take = 2,
        })).ToList();

        var page2 = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Skip = 2,
            Take = 2,
        })).ToList();

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);

        var allIds = page1.Concat(page2).Select(r => r.ResourceId).ToList();
        Assert.Equal(4, allIds.Distinct().Count()); // no duplicates
        Assert.Contains("res-002", allIds); // null-owner included
        Assert.Contains("res-004", allIds); // null-owner included
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Null hash field — still round-trips
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NullHash_RoundTrips()
    {
        await SeedMixedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("ResourceId", "res-004", ComparisonOperator.Equals),
        })).ToList();

        Assert.Single(results);
        Assert.Null(results[0].Hash);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Mixed null/present owner with combined filter
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CombinedFilter_NullOwnerResources_NotIncludedInOwnerEqualsFilter()
    {
        await SeedMixedResources();

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
        })).ToList();

        // Only res-001 matches (Product + Owner=alice)
        Assert.Single(results);
        Assert.Equal("res-001", results[0].ResourceId);
    }
}
