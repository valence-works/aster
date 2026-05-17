using Aster.Core.Abstractions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Querying;

public sealed class ResourceQueryValidatorTests
{
    private readonly ResourceQueryValidator inMemoryValidator = new([new InMemoryQueryCapabilitiesProvider()]);
    private readonly ResourceQueryValidator sqliteValidator = new([new SqliteJsonQueryCapabilitiesProvider()]);

    [Fact]
    public void Validate_WithSupportedQuery_ReturnsSuccessWithoutMutatingQuery()
    {
        var query = new ResourceQuery
        {
            Scope = ResourceVersionScope.Latest,
            Filter = new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
            Sorts = [new SortExpression("Created", SortDirection.Descending)],
            Skip = 1,
            Take = 10,
        };

        var expected = new ResourceQuery
        {
            Scope = ResourceVersionScope.Latest,
            Filter = new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
            Sorts = [new SortExpression("Created", SortDirection.Descending)],
            Skip = 1,
            Take = 10,
        };
        var result = sqliteValidator.Validate(query);

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
        Assert.Equal(expected.Scope, query.Scope);
        Assert.Equal(expected.Filter, query.Filter);
        Assert.Equal(expected.Sorts, query.Sorts);
        Assert.Equal(expected.Skip, query.Skip);
        Assert.Equal(expected.Take, query.Take);
    }

    [Fact]
    public void Validate_WithMultipleUnsupportedFeatures_ReturnsAllDetectableFailures()
    {
        var result = sqliteValidator.Validate(new ResourceQuery
        {
            Scope = ResourceVersionScope.Active,
            Skip = -1,
            Take = -1,
            Filter = new LogicalExpression(LogicalOperator.And, [
                new MetadataFilter("Unknown", "value", ComparisonOperator.Range),
                new FacetValueFilter(
                    "Price",
                    "Amount",
                    new RangeValue(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
                    ComparisonOperator.Range),
            ]),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "activation-channel-required");
        Assert.Contains(result.Failures, failure => failure.Code == "negative-skip");
        Assert.Contains(result.Failures, failure => failure.Code == "negative-take");
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-metadata-field");
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-comparison-operator");
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-range-value-shape");
    }

    [Fact]
    public void Validate_WithoutCapabilities_FailsClosed()
    {
        var result = new ResourceQueryValidator([]).Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains("not declared", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ManualResourceQueriesRemainSupported()
    {
        var query = new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = new LogicalExpression(LogicalOperator.And, [
                new AspectPresenceFilter("Title"),
                new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
            ]),
        };

        var result = inMemoryValidator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InMemoryAndSqliteReflectProviderDifferences()
    {
        var dateRangeQuery = new ResourceQuery
        {
            Filter = new FacetValueFilter(
                "Schedule",
                "StartsAt",
                new RangeValue(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
                ComparisonOperator.Range),
            Sorts = [new SortExpression("StartsAt", AspectKey: "Schedule")],
        };

        Assert.True(inMemoryValidator.Validate(dateRangeQuery).IsValid);

        var sqliteResult = sqliteValidator.Validate(dateRangeQuery);

        Assert.False(sqliteResult.IsValid);
        Assert.Contains(sqliteResult.Failures, failure => failure.Code == "unsupported-range-value-shape");
    }

    [Fact]
    public void Validate_MetadataCapabilitiesMatchProviderExecution()
    {
        var sqliteMetadataContains = sqliteValidator.Validate(new ResourceQuery
        {
            Filter = new MetadataFilter("Version", "1", ComparisonOperator.Contains),
        });
        var inMemoryMetadataRange = inMemoryValidator.Validate(new ResourceQuery
        {
            Filter = new MetadataFilter("Created", "2026-01-01", ComparisonOperator.Range),
        });

        Assert.False(sqliteMetadataContains.IsValid);
        Assert.Contains(sqliteMetadataContains.Failures, failure => failure.Code == "unsupported-metadata-contains-field");
        Assert.False(inMemoryMetadataRange.IsValid);
        Assert.Contains(inMemoryMetadataRange.Failures, failure => failure.Code == "unsupported-comparison-operator");
    }

    [Fact]
    public void Validate_NewPortableOperators_UseCapabilityDeclarations()
    {
        var result = sqliteValidator.Validate(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.And, [
                new MetadataFilter("Owner", "bob", ComparisonOperator.NotEquals),
                new MetadataFilter("Owner", new[] { "alice", "carol" }, ComparisonOperator.In),
                new MetadataFilter("Owner", "al", ComparisonOperator.StartsWith),
                new FacetValueFilter("Title", "Title", "Beta", ComparisonOperator.NotEquals),
                new FacetValueFilter("Category", "Category", new[] { "Hardware", "Electronics" }, ComparisonOperator.In),
                new FacetValueFilter("Title", "Title", "Al", ComparisonOperator.StartsWith),
                new FacetValueFilter("Title", "Title", true, ComparisonOperator.Exists),
            ]),
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null, "in-values-required")]
    [InlineData("abc", "in-values-required")]
    public void Validate_InOperatorRejectsInvalidValueShapes(object? value, string expectedCode)
    {
        var result = sqliteValidator.Validate(new ResourceQuery
        {
            Filter = new FacetValueFilter("Category", "Category", value!, ComparisonOperator.In),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == expectedCode);
    }

    [Fact]
    public void Validate_InOperatorRejectsEmptyValueSet()
    {
        var result = sqliteValidator.Validate(new ResourceQuery
        {
            Filter = new MetadataFilter("Owner", Array.Empty<string>(), ComparisonOperator.In),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "empty-in-values");
    }

    [Theory]
    [InlineData(ComparisonOperator.Contains)]
    [InlineData(ComparisonOperator.StartsWith)]
    public void Validate_TextOperatorsRejectNullValues(ComparisonOperator comparisonOperator)
    {
        var result = sqliteValidator.Validate(new ResourceQuery
        {
            Filter = new FacetValueFilter("Title", "Title", null!, comparisonOperator),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "text-value-required");
    }

    [Fact]
    public void Validate_RejectsInvalidSortDirection()
    {
        var result = sqliteValidator.Validate(new ResourceQuery
        {
            Sorts = [new SortExpression("Created", (SortDirection)999)],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-sort-direction");
    }

    [Fact]
    public void Validate_RejectsDateOnlyRangeBounds()
    {
        var result = inMemoryValidator.Validate(new ResourceQuery
        {
            Filter = new FacetValueFilter(
                "Schedule",
                "StartsOn",
                new RangeValue(DateOnly.FromDateTime(DateTime.UtcNow), null),
                ComparisonOperator.Range),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-range-value-shape");
    }

    [Fact]
    public void Validate_WhenActiveQueryServiceHasNoMatchingCapabilities_FailsClosed()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IResourceQueryCapabilitiesProvider, InMemoryQueryCapabilitiesProvider>()
            .AddSingleton<IResourceQueryService, CustomQueryService>()
            .AddSingleton<IResourceQueryValidator, ResourceQueryValidator>()
            .BuildServiceProvider();

        var result = provider.GetRequiredService<IResourceQueryValidator>().Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains("custom", failure.Message);
        Assert.Contains("matching ProviderKey", failure.Message);
    }

    [Fact]
    public void Validate_WhenActiveQueryServiceHasNoProviderIdentity_FailsClosedWithIdentityGuidance()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IResourceQueryCapabilitiesProvider, InMemoryQueryCapabilitiesProvider>()
            .AddSingleton<IResourceQueryService, QueryServiceWithoutProviderIdentity>()
            .AddSingleton<IResourceQueryValidator, ResourceQueryValidator>()
            .BuildServiceProvider();

        var result = provider.GetRequiredService<IResourceQueryValidator>().Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains("IResourceQueryProviderIdentity", failure.Message);
        Assert.Contains("matching ProviderKey", failure.Message);
    }

    [Fact]
    public void Validate_WhenActiveProviderKeyDoesNotMatchCapabilities_FailsClosed()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IResourceQueryCapabilitiesProvider, EmptyCapabilitiesProvider>()
            .AddSingleton<IResourceQueryService, CustomQueryService>()
            .AddSingleton<IResourceQueryValidator, ResourceQueryValidator>()
            .BuildServiceProvider();

        var result = provider.GetRequiredService<IResourceQueryValidator>().Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains("custom", failure.Message);
        Assert.Contains("matching ProviderKey", failure.Message);
    }

    [Fact]
    public void Validate_WhenActiveProviderKeyIsEmpty_FailsClosedWithProviderKeyGuidance()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IResourceQueryCapabilitiesProvider, InMemoryQueryCapabilitiesProvider>()
            .AddSingleton<IResourceQueryService, EmptyProviderKeyQueryService>()
            .AddSingleton<IResourceQueryValidator, ResourceQueryValidator>()
            .BuildServiceProvider();

        var result = provider.GetRequiredService<IResourceQueryValidator>().Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains("ProviderKey is empty", failure.Message);
        Assert.Contains("non-empty ProviderKey", failure.Message);
    }

    private sealed class EmptyCapabilitiesProvider : IResourceQueryCapabilitiesProvider
    {
        public QueryCapabilityDescription Capabilities { get; } = new(
            ProviderKey: "empty",
            ProviderName: "Empty",
            SupportedScopes: new HashSet<ResourceVersionScope>(),
            RequiresActivationChannelForActiveScope: false,
            SupportedFilterTypes: new HashSet<QueryFilterType>(),
            SupportedLogicalOperators: new HashSet<LogicalOperator>(),
            SupportedComparisonOperators: new Dictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>>(),
            SupportedMetadataFields: new HashSet<string>(),
            MetadataContainsFields: new HashSet<string>(),
            SupportsMetadataSorting: false,
            SupportsFacetSorting: false,
            SupportsSkip: false,
            SupportsTake: false,
            FacetRangeSupport: new HashSet<QueryValueShape>(),
            UnsupportedFeatures: []);
    }

    private sealed class CustomQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        public string ProviderKey => "custom";

        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default) =>
            new([]);
    }

    private sealed class QueryServiceWithoutProviderIdentity : IResourceQueryService
    {
        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default) =>
            new([]);
    }

    private sealed class EmptyProviderKeyQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        public string ProviderKey => " ";

        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default) =>
            new([]);
    }
}
