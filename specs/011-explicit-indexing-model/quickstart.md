# Quickstart: Explicit Indexing Model

## Inspect Provider Index Declarations

Built-in providers declare no default index projections in this slice:

```csharp
var capabilities = serviceProvider
    .GetRequiredService<IResourceQueryCapabilitiesProvider>()
    .Capabilities;

foreach (var projection in capabilities.IndexProjections)
{
    Console.WriteLine($"{projection.FieldName}: {projection.FieldType}");
}
```

## Declare Projections In A Custom Provider

Custom providers can declare explicit projections through their capability description:

```csharp
public sealed class SearchQueryCapabilitiesProvider : IResourceQueryCapabilitiesProvider
{
    public QueryCapabilityDescription Capabilities { get; } = new(
        ProviderKey: "search",
        ProviderName: "Search",
        SupportedScopes: [ResourceVersionScope.Latest],
        RequiresActivationChannelForActiveScope: true,
        SupportedFilterTypes: [],
        SupportedLogicalOperators: [],
        SupportedComparisonOperators: new Dictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>>(),
        SupportedMetadataFields: [],
        MetadataContainsFields: [],
        SupportsMetadataSorting: false,
        SupportsFacetSorting: false,
        SupportsSkip: false,
        SupportsTake: false,
        FacetRangeSupport: [],
        UnsupportedFeatures: [],
        IndexProjections:
        [
            IndexProjection.Metadata("resource_id", "ResourceId", IndexFieldType.Keyword),
            IndexProjection.Facet("title", "Title", "Title", IndexFieldType.NormalizedText),
            IndexProjection.Facet("tags", "Taxonomy", "Tags", IndexFieldType.KeywordArray),
        ]);
}
```

## Evaluate Projections For A Resource Version

Projection evaluation returns successful values and failures together:

```csharp
var projectionEvaluator = new IndexProjectionEvaluator();
var result = projectionEvaluator.Evaluate(resource, capabilities.IndexProjections);

foreach (var value in result.Values)
{
    Console.WriteLine($"{value.FieldName}: {value.Value}");
}

foreach (var failure in result.Failures)
{
    Console.WriteLine($"{failure.FieldName}: {failure.Code} - {failure.Message}");
}
```

`IndexProjectionEvaluator` is explicit SDK helper code, not provider discovery. Providers can run `IndexProjectionValidator` at startup or in conformance tests to catch invalid declarations before resource evaluation.

## Boundaries

This slice does not add physical indexes, migrations, query planning, runtime scanning, automatic discovery, public SQL, public `IQueryable<Resource>`, or resource-definition index annotations.
