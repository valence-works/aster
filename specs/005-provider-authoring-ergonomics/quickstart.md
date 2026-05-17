# Quickstart: Provider Authoring Ergonomics

## Define Provider Identity And Execution

```csharp
public sealed class MyQueryService : IResourceQueryService, IResourceQueryProviderIdentity
{
    private readonly ResourceQueryValidator validator;

    public MyQueryService(IEnumerable<IResourceQueryCapabilitiesProvider> capabilityProviders)
    {
        validator = new(capabilityProviders, this);
    }

    public string ProviderKey => "my-provider";

    public ValueTask<IEnumerable<Resource>> QueryAsync(
        ResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(query);
        if (!validation.IsValid)
            throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);

        // Keep provider-specific checks authoritative during translation/execution.
        return new([]);
    }
}
```

## Declare Matching Capabilities

```csharp
public sealed class MyQueryCapabilitiesProvider : IResourceQueryCapabilitiesProvider
{
    public QueryCapabilityDescription Capabilities { get; } = new(
        ProviderKey: "my-provider",
        ProviderName: "My Provider",
        SupportedScopes: new HashSet<ResourceVersionScope> { ResourceVersionScope.Latest },
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
```

## Register Provider

```csharp
services
    .AddAsterCore()
    .AddResourceQueryProvider<MyQueryService, MyQueryCapabilitiesProvider>();
```

The helper registers the query service, provider identity, and capability provider together as singletons for both concrete types and shared interfaces. The custom provider becomes the active query provider by normal DI last-registration-wins behavior. Use manual DI registration when a host needs alternate lifetimes.

## Validate And Execute

```csharp
var validator = serviceProvider.GetRequiredService<IResourceQueryValidator>();
var validation = validator.Validate(query);

if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code} ({failure.Feature}): {failure.Message}");

    return;
}

var queryService = serviceProvider.GetRequiredService<IResourceQueryService>();
var results = await queryService.QueryAsync(query);
```

## Troubleshoot Missing Capabilities

If validation returns `capabilities-not-declared`, confirm:

- The active query provider implements `IResourceQueryProviderIdentity`.
- The active provider key is non-empty.
- An `IResourceQueryCapabilitiesProvider` is registered.
- The capability declaration `ProviderKey` exactly matches the active provider key.

## Provider-Specific Execution Failure

```csharp
throw new UnsupportedQueryFeatureException(
    code: "unsupported-custom-value",
    feature: "value shape",
    message: "This provider cannot execute the supplied custom value shape.",
    path: "Filter.Value");
```

Execution should remain authoritative even when validation succeeds.
