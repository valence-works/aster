using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Querying;

public sealed class TypedQueryHelperTests
{
    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount);
    private sealed record NestedAspect(TitleAspect Nested);

    private static string StaticTitle => "Static";

    [Fact]
    public void HasAspect_UsesAspectTypeNameByConvention()
    {
        var filter = Assert.IsType<AspectPresenceFilter>(TypedQuery.HasAspect<TitleAspect>());

        Assert.Equal("TitleAspect", filter.AspectKey);
    }

    [Fact]
    public void FacetHelpers_UseAspectAndMemberNamesByConvention()
    {
        var equality = Assert.IsType<FacetValueFilter>(TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .EqualTo("Gadget"));
        var inequality = Assert.IsType<FacetValueFilter>(TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .NotEqualTo("Widget"));
        var membership = Assert.IsType<FacetValueFilter>(TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .In("Gadget", "Widget"));
        var contains = Assert.IsType<FacetValueFilter>(TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .Contains("Gadget"));
        var startsWith = Assert.IsType<FacetValueFilter>(TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .StartsWith("Gad"));
        var exists = Assert.IsType<FacetValueFilter>(TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .Exists());
        var range = Assert.IsType<FacetValueFilter>(TypedQuery.For<PriceAspect>()
            .Facet(aspect => aspect.Amount)
            .Range(10m, 20m));

        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.Equals), (equality.AspectKey, equality.FacetDefinitionId, equality.Operator));
        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.NotEquals), (inequality.AspectKey, inequality.FacetDefinitionId, inequality.Operator));
        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.In), (membership.AspectKey, membership.FacetDefinitionId, membership.Operator));
        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.Contains), (contains.AspectKey, contains.FacetDefinitionId, contains.Operator));
        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.StartsWith), (startsWith.AspectKey, startsWith.FacetDefinitionId, startsWith.Operator));
        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.Exists), (exists.AspectKey, exists.FacetDefinitionId, exists.Operator));
        Assert.Equal(("PriceAspect", "Amount", ComparisonOperator.Range), (range.AspectKey, range.FacetDefinitionId, range.Operator));
        Assert.Equal(new[] { "Gadget", "Widget" }, Assert.IsType<string[]>(membership.Value));
        Assert.True(Assert.IsType<bool>(exists.Value));
        Assert.Equal(new RangeValue(10m, 20m), range.Value);
    }

    [Fact]
    public void Range_WithOnlyMaxBound_LeavesMinimumUnbounded()
    {
        var filter = Assert.IsType<FacetValueFilter>(TypedQuery.For<PriceAspect>()
            .Facet(aspect => aspect.Amount)
            .Range(max: 100m));

        var range = Assert.IsType<RangeValue>(filter.Value);
        Assert.Null(range.Min);
        Assert.Equal(100m, range.Max);
    }

    [Fact]
    public void FacetHelpers_UsePerQueryOverrides()
    {
        var filter = Assert.IsType<FacetValueFilter>(TypedQuery.For<PriceAspect>(aspectKey: "PriceAspect:Sale")
            .Facet(aspect => aspect.Amount, facetIdentifier: "sale_amount")
            .Range(10m, 100m));

        Assert.Equal("PriceAspect:Sale", filter.AspectKey);
        Assert.Equal("sale_amount", filter.FacetDefinitionId);
    }

    [Fact]
    public void SortHelpers_UseAspectAndMemberNamesByConvention()
    {
        var ascending = TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .Ascending();
        var descending = TypedQuery.For<PriceAspect>()
            .Facet(aspect => aspect.Amount)
            .Descending();

        Assert.Equal(new SortExpression("Title", SortDirection.Ascending, "TitleAspect"), ascending);
        Assert.Equal(new SortExpression("Amount", SortDirection.Descending, "PriceAspect"), descending);
    }

    [Fact]
    public void SortHelpers_UsePerQueryOverrides()
    {
        var sort = TypedQuery.For<PriceAspect>(aspectKey: "PriceAspect:Sale")
            .Facet(aspect => aspect.Amount, facetIdentifier: "sale_amount")
            .Descending();

        Assert.Equal(new SortExpression("sale_amount", SortDirection.Descending, "PriceAspect:Sale"), sort);
    }

    [Fact]
    public void FacetHelpers_UseOptionsOverrides()
    {
        var options = new TypedQueryOptions(
            AspectKey: "PriceAspect:Wholesale",
            FacetIdentifier: "wholesale_amount");

        var filter = Assert.IsType<FacetValueFilter>(TypedQuery.For<PriceAspect>(options)
            .Facet(aspect => aspect.Amount)
            .Range(10m, 100m));

        Assert.Equal("PriceAspect:Wholesale", filter.AspectKey);
        Assert.Equal("wholesale_amount", filter.FacetDefinitionId);
    }

    [Fact]
    public void Facet_WithNonMemberSelector_FailsClearly()
    {
        var exception = Assert.Throws<ArgumentException>(() => TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title.ToUpperInvariant()));

        Assert.Contains("single readable member", exception.Message);
    }

    [Fact]
    public void Facet_WithOverrideStillValidatesSelector()
    {
        var exception = Assert.Throws<ArgumentException>(() => TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title.ToUpperInvariant(), facetIdentifier: "Title"));

        Assert.Contains("single readable member", exception.Message);
    }

    [Fact]
    public void Sort_WithOverrideStillValidatesSelector()
    {
        var exception = Assert.Throws<ArgumentException>(() => TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title.ToUpperInvariant(), facetIdentifier: "Title")
            .Ascending());

        Assert.Contains("single readable member", exception.Message);
    }

    [Fact]
    public void Facet_WithNonDirectMemberSelector_FailsClearly()
    {
        Assert.Throws<ArgumentException>(() => TypedQuery.For<NestedAspect>()
            .Facet(aspect => aspect.Nested.Title));

        Assert.Throws<ArgumentException>(() => TypedQuery.For<TitleAspect>()
            .Facet(_ => StaticTitle));
    }

    [Fact]
    public void TypedHelperOutput_ValidatesLikeManualQuery()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        var validator = provider.GetRequiredService<IResourceQueryValidator>();

        var result = validator.Validate(new ResourceQuery
        {
            Filter = TypedQuery.For<TitleAspect>()
                .Facet(aspect => aspect.Title)
                .Contains("Gadget"),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void LogicalHelpers_CreateLogicalExpressions()
    {
        var title = TypedQuery.For<TitleAspect>().Facet(aspect => aspect.Title).Contains("Gadget");
        var price = TypedQuery.For<PriceAspect>().Facet(aspect => aspect.Amount).Range(max: 100m);

        var and = Assert.IsType<LogicalExpression>(TypedQuery.And(title, price));
        var or = Assert.IsType<LogicalExpression>(TypedQuery.Or(title, price));
        var not = Assert.IsType<LogicalExpression>(TypedQuery.Not(title));

        Assert.Equal(LogicalOperator.And, and.Operator);
        Assert.Equal([title, price], and.Operands);
        Assert.Equal(LogicalOperator.Or, or.Operator);
        Assert.Equal([title, price], or.Operands);
        Assert.Equal(LogicalOperator.Not, not.Operator);
        Assert.Equal([title], not.Operands);
    }

    [Fact]
    public void LogicalHelpers_RejectInvalidOperandSets()
    {
        Assert.Throws<ArgumentException>(() => TypedQuery.And());
        Assert.Throws<ArgumentException>(() => TypedQuery.Or());
        Assert.Throws<ArgumentNullException>(() => TypedQuery.Not(null!));
        Assert.Throws<ArgumentException>(() => TypedQuery.And([null!]));
        Assert.Throws<ArgumentException>(() => TypedQuery.Or([null!]));
    }

    [Fact]
    public async Task TypedHelperGeneratedQuery_ExecutesAgainstSupportedProvider()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        var manager = provider.GetRequiredService<IResourceManager>();
        var query = provider.GetRequiredService<IResourceQueryService>();

        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["TitleAspect"] = new TitleAspect("Super Gadget") },
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["TitleAspect"] = new TitleAspect("Regular Widget") },
        });

        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = TypedQuery.For<TitleAspect>()
                .Facet(aspect => aspect.Title)
                .Contains("Gadget"),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("Product", results[0].DefinitionId);
    }

    [Fact]
    public async Task TypedSortGeneratedQuery_ExecutesAgainstSupportedProvider()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        var manager = provider.GetRequiredService<IResourceManager>();
        var query = provider.GetRequiredService<IResourceQueryService>();
        var binder = provider.GetRequiredService<ITypedAspectBinder>();

        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["TitleAspect"] = new TitleAspect("Bravo") },
        });
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new() { ["TitleAspect"] = new TitleAspect("Alpha") },
        });

        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = TypedQuery.HasAspect<TitleAspect>(),
            Sorts =
            [
                TypedQuery.For<TitleAspect>()
                    .Facet(aspect => aspect.Title)
                    .Ascending(),
            ],
        })).ToList();

        Assert.Equal(["Alpha", "Bravo"], results
            .Select(resource => resource.GetAspect<TitleAspect>("TitleAspect", binder)!.Title)
            .ToList());
    }

    [Fact]
    public void PublicAbstractions_DoNotExposeQueryableResourceProvider()
    {
        var publicTypes = typeof(IResourceQueryService).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.Namespace == "Aster.Core.Abstractions");

        Assert.DoesNotContain(publicTypes, type => typeof(IQueryable<Resource>).IsAssignableFrom(type));
    }
}
