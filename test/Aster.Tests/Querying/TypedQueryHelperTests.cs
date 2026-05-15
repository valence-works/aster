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
        var contains = Assert.IsType<FacetValueFilter>(TypedQuery.For<TitleAspect>()
            .Facet(aspect => aspect.Title)
            .Contains("Gadget"));
        var range = Assert.IsType<FacetValueFilter>(TypedQuery.For<PriceAspect>()
            .Facet(aspect => aspect.Amount)
            .Range(10m, 20m));

        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.Equals), (equality.AspectKey, equality.FacetDefinitionId, equality.Operator));
        Assert.Equal(("TitleAspect", "Title", ComparisonOperator.Contains), (contains.AspectKey, contains.FacetDefinitionId, contains.Operator));
        Assert.Equal(("PriceAspect", "Amount", ComparisonOperator.Range), (range.AspectKey, range.FacetDefinitionId, range.Operator));
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
    public void PublicAbstractions_DoNotExposeQueryableResourceProvider()
    {
        var publicTypes = typeof(IResourceQueryService).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.Namespace == "Aster.Core.Abstractions");

        Assert.DoesNotContain(publicTypes, type => typeof(IQueryable<Resource>).IsAssignableFrom(type));
    }
}
