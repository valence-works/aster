using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Tests.Persistence;

/// <summary>
/// Tests for deterministic paging and sorting including tie-break on
/// (<c>ResourceId</c>, <c>Version</c>) via the Sqlite provider.
/// </summary>
public sealed class SqliteQueryPagingSortingTests : IDisposable
{
    private sealed record TitleAspect(string Title);

    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    /// <summary>
    /// Seeds N single-version resources with deterministic resource IDs for sorting tests.
    /// </summary>
    private async Task SeedSortableResources(int count = 10)
    {
        for (var i = 0; i < count; i++)
        {
            var resource = new Resource
            {
                ResourceId = $"res-{i:D4}",
                Id = $"v-{i:D4}-1",
                DefinitionId = "Product",
                Version = 1,
                Created = DateTime.UtcNow,
                Owner = $"owner-{i % 3}",
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect($"Item {i:D4}") },
                },
            };
            await fixture.WriteStore.SaveVersionAsync(resource);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Basic paging — Take
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Take_LimitsResultCount()
    {
        await SeedSortableResources(10);

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Take = 3,
        })).ToList();

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Take_ExceedsTotalCount_ReturnsAll()
    {
        await SeedSortableResources(5);

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Take = 100,
        })).ToList();

        Assert.Equal(5, results.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Basic paging — Skip
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Skip_OmitsFirstNResults()
    {
        await SeedSortableResources(10);

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Skip = 7,
        })).ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal("res-0007", results[0].ResourceId);
    }

    [Fact]
    public async Task Skip_BeyondTotal_ReturnsEmpty()
    {
        await SeedSortableResources(5);

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Skip = 100,
        })).ToList();

        Assert.Empty(results);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Combined Take + Skip (page windows)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TakeSkip_ReturnsCorrectPage()
    {
        await SeedSortableResources(10);

        // Page 2 (size 3): items at indices 3,4,5
        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Skip = 3,
            Take = 3,
        })).ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal("res-0003", results[0].ResourceId);
        Assert.Equal("res-0004", results[1].ResourceId);
        Assert.Equal("res-0005", results[2].ResourceId);
    }

    [Fact]
    public async Task TakeSkip_LastPartialPage_ReturnsRemainder()
    {
        await SeedSortableResources(10);

        // Page 4 (size 3): only item at index 9
        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Skip = 9,
            Take = 3,
        })).ToList();

        Assert.Single(results);
        Assert.Equal("res-0009", results[0].ResourceId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Deterministic sort order — (ResourceId ASC, Version ASC)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Results_AreSortedByResourceIdAscending()
    {
        await SeedSortableResources(10);

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery())).ToList();

        for (var i = 1; i < results.Count; i++)
        {
            Assert.True(
                string.Compare(results[i - 1].ResourceId, results[i].ResourceId, StringComparison.Ordinal) <= 0,
                $"Expected {results[i - 1].ResourceId} <= {results[i].ResourceId}");
        }
    }

    [Fact]
    public async Task ConsecutivePages_ProduceDeterministicNonOverlappingResults()
    {
        await SeedSortableResources(10);

        var page1 = (await fixture.QueryService.QueryAsync(new ResourceQuery { Skip = 0, Take = 5 })).ToList();
        var page2 = (await fixture.QueryService.QueryAsync(new ResourceQuery { Skip = 5, Take = 5 })).ToList();

        Assert.Equal(5, page1.Count);
        Assert.Equal(5, page2.Count);

        var allIds = page1.Concat(page2).Select(r => r.ResourceId).ToList();
        Assert.Equal(10, allIds.Distinct().Count()); // no duplicates
    }

    [Fact]
    public async Task StableSort_MultipleCalls_ReturnSameOrder()
    {
        await SeedSortableResources(10);

        var first = (await fixture.QueryService.QueryAsync(new ResourceQuery())).Select(r => r.ResourceId).ToList();
        var second = (await fixture.QueryService.QueryAsync(new ResourceQuery())).Select(r => r.ResourceId).ToList();

        Assert.Equal(first, second);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Paging with filter
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TakeSkip_WithFilter_AppliedAfterFiltering()
    {
        await SeedSortableResources(10);

        // Filter to owner-0 (indices 0,3,6,9) then page
        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("Owner", "owner-0", ComparisonOperator.Equals),
            Skip = 1,
            Take = 2,
        })).ToList();

        Assert.Equal(2, results.Count);
        // After filtering to owner-0 sorted: res-0000, res-0003, res-0006, res-0009
        // Skip 1, Take 2 → res-0003, res-0006
        Assert.Equal("res-0003", results[0].ResourceId);
        Assert.Equal("res-0006", results[1].ResourceId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  No paging returns all
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoPaging_ReturnsAllResults()
    {
        await SeedSortableResources(10);

        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery())).ToList();

        Assert.Equal(10, results.Count);
    }
}
