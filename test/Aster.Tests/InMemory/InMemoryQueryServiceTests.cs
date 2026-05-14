using Aster.Core.Abstractions;
using Aster.Core.InMemory;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aster.Tests.InMemory;

public sealed class InMemoryQueryServiceTests
{
    private sealed record TitleAspect(string Title);
    private sealed record CategoryAspect(string Category);

    private static (InMemoryResourceManager Manager, InMemoryQueryService Query) CreateSetup()
    {
        var store = new InMemoryResourceStore();
        var defStore = Substitute.For<IResourceDefinitionStore>();
        var manager = new InMemoryResourceManager(store, defStore, new GuidIdentityGenerator(), NullLogger<InMemoryResourceManager>.Instance);
        var query = new InMemoryQueryService(store, NullLogger<InMemoryQueryService>.Instance);
        return (manager, query);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DefinitionId filter
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_WithDefinitionIdFilter_ReturnsOnlyMatchingType()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = new() { ["Title"] = new TitleAspect("Gadget") } });
        await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = new() { ["Title"] = new TitleAspect("Widget") } });
        await manager.CreateAsync("Order", new CreateResourceRequest());

        // Act
        var results = (await query.QueryAsync(new ResourceQuery { DefinitionId = "Product" })).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("Product", r.DefinitionId));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Aspect presence filter
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_AspectPresenceFilter_ReturnsResourcesWithAspect()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = new() { ["Title"] = new TitleAspect("Has Title") } });
        await manager.CreateAsync("Product", new CreateResourceRequest()); // no Title aspect

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new AspectPresenceFilter("Title")
        })).ToList();

        // Assert
        Assert.Single(results);
        Assert.True(results[0].Aspects.ContainsKey("Title"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FacetValue filter — Contains
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_FacetValueContainsFilter_ReturnsMatchingResources()
    {
        // Arrange
        var (manager, query) = CreateSetup();

        // Create via AspectBinder to store as JSON string
        var binder = new SystemTextJsonAspectBinder();

        // Create 3 products using dictionary-stored title aspect
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Super Gadget") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Regular Widget") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Another Gadget") }
        });

        // Serialize stored values so the query evaluator can find them
        // (In real usage the aspect would be serialized by the binder during SetAspect;
        //  here we ensure the POCO fast-path fallback works for TitleAspect POCOs)

        // Act — filter products whose Title facet contains "Gadget"
        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains)
        })).ToList();

        // Assert
        Assert.Equal(2, results.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FacetValue filter — Equals
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_FacetValueEqualsFilter_ReturnsExactMatch()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Category"] = new CategoryAspect("Electronics") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Category"] = new CategoryAspect("Clothing") }
        });

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("Category", "Category", "Electronics", ComparisonOperator.Equals)
        })).ToList();

        // Assert
        Assert.Single(results);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AND / OR composition
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_AndComposition_BothConditionsMustMatch()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Gadget"), ["Category"] = new CategoryAspect("Electronics") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Widget"), ["Category"] = new CategoryAspect("Electronics") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Gadget"), ["Category"] = new CategoryAspect("Clothing") }
        });

        // Act — Title contains "Gadget" AND Category equals "Electronics"
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.And, [
                new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
                new FacetValueFilter("Category", "Category", "Electronics", ComparisonOperator.Equals)
            ])
        })).ToList();

        // Assert — only first resource matches both conditions
        Assert.Single(results);
    }

    [Fact]
    public async Task QueryAsync_OrComposition_EitherConditionMatches()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Gadget") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Widget") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Other") }
        });

        // Act — Title contains "Gadget" OR Title contains "Widget"
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.Or, [
                new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
                new FacetValueFilter("Title", "Title", "Widget", ComparisonOperator.Contains)
            ])
        })).ToList();

        // Assert
        Assert.Equal(2, results.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Range operator
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_RangeOperator_ReturnsValuesWithinBounds()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Price"] = new { Amount = 10 } }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Price"] = new { Amount = 20 } }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Price"] = new { Amount = 30 } }
        });

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter(
                "Price",
                "Amount",
                new RangeValue(Min: 15, Max: 25),
                ComparisonOperator.Range)
        })).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal(20, ((dynamic)results[0].Aspects["Price"]).Amount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Metadata filter
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_MetadataFilter_DefinitionIdEquals_FiltersCorrectly()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.CreateAsync("Order", new CreateResourceRequest());

        // Act — use MetadataFilter instead of shortcut DefinitionId
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("DefinitionId", "Order", ComparisonOperator.Equals)
        })).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("Order", results[0].DefinitionId);
    }

    [Fact]
    public async Task QueryAsync_AllVersionsScope_ReturnsHistoricalVersions()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("V1") }
        });
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = new() { ["Title"] = new TitleAspect("V2") }
        });

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Scope = ResourceVersionScope.AllVersions,
            DefinitionId = "Product",
            Sorts = [new SortExpression("Version")]
        })).ToList();

        // Assert
        Assert.Equal([1, 2], results.Select(r => r.Version).ToList());
    }

    [Fact]
    public async Task QueryAsync_ActiveScope_ReturnsVersionsActiveInChannel()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.ActivateAsync(v1.ResourceId, 1, "Published");

        await manager.CreateAsync("Product", new CreateResourceRequest());

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
            DefinitionId = "Product"
        })).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal(v1.ResourceId, results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_DraftScope_ReturnsVersionsNotActiveInAnyChannel()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        var active = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.ActivateAsync(active.ResourceId, 1, "Published");

        var draft = await manager.CreateAsync("Product", new CreateResourceRequest());

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Scope = ResourceVersionScope.Draft,
            DefinitionId = "Product"
        })).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal(draft.ResourceId, results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_SortsByFacetValue()
    {
        // Arrange
        var (manager, query) = CreateSetup();
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Bravo") }
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["Title"] = new TitleAspect("Alpha") }
        });

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Sorts = [new SortExpression("Title", AspectKey: "Title")]
        })).ToList();

        // Assert
        Assert.Equal(["Alpha", "Bravo"], results.Select(r => ((TitleAspect)r.Aspects["Title"]).Title).ToList());
    }
}
