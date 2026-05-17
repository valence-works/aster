# Contract: Provider Authoring Ergonomics

This contract describes the public SDK behavior expected by this feature. Names are final unless implementation reveals an existing naming conflict.

## Registration Helper

Provider authors can register a custom query provider and capabilities together.

```csharp
public static IServiceCollection AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>(
    this IServiceCollection services)
    where TQueryService : class, IResourceQueryService, IResourceQueryProviderIdentity
    where TCapabilitiesProvider : class, IResourceQueryCapabilitiesProvider;
```

Expected behavior:

- Registers `TQueryService` as a concrete singleton.
- Registers `TQueryService` as the active singleton `IResourceQueryService`.
- Registers `TQueryService` as singleton `IResourceQueryProviderIdentity`.
- Registers `TCapabilitiesProvider` as a concrete singleton.
- Registers `TCapabilitiesProvider` as singleton `IResourceQueryCapabilitiesProvider`.
- Leaves alternate lifetimes to explicit manual DI registration.
- Preserves existing last-registration-wins behavior for active query provider replacement.
- Does not instantiate services during registration.
- Does not validate provider key equality during registration.

## Provider Identity

Custom query providers expose explicit identity.

```csharp
public sealed class MyQueryService : IResourceQueryService, IResourceQueryProviderIdentity
{
    public string ProviderKey => "my-provider";
}
```

Expected behavior:

- Provider key must be stable and non-empty.
- Provider key must match the capability declaration key for validation to succeed.
- Decorators may expose their own key or pass through the wrapped provider key intentionally.

## Capability Declaration

Custom providers declare query capabilities using the same provider key.

```csharp
public sealed class MyQueryCapabilitiesProvider : IResourceQueryCapabilitiesProvider
{
    public QueryCapabilityDescription Capabilities { get; } = new(
        ProviderKey: "my-provider",
        ProviderName: "My Provider",
        /* supported query surface */);
}
```

Expected behavior:

- Matching key means validation uses the custom declaration.
- Missing or mismatched key means validation fails closed.
- Built-in capability declarations remain unchanged.

## Fail-Closed Diagnostics

Validation remains non-throwing.

```csharp
var validation = validator.Validate(query);
```

Expected behavior:

- Missing/mismatched active provider capabilities return `capabilities-not-declared`.
- Failure message identifies the active provider key when available.
- Failure message tells authors to register a matching capability declaration.

## Execution Guidance

Custom providers should run shared validation before execution, then keep provider-specific checks.

Expected behavior:

- Validation-derived execution failures use `UnsupportedQueryFeatureException.FromValidationFailure`.
- Provider-specific execution failures use structured `UnsupportedQueryFeatureException` data.
- Execution remains authoritative even when caller preflight is skipped.
