# Contract: Provider Validation Execution Alignment

This contract describes the public SDK behavior expected by the feature. Names are provisional for planning, but behavior is normative.

## Provider Identity

Query providers expose an explicit provider key used to match capability declarations.

```csharp
public interface IResourceQueryProviderIdentity
{
    string ProviderKey { get; }
}
```

Expected behavior:

- The in-memory query provider uses a stable key such as `in-memory`.
- The SQLite JSON query provider uses a stable key such as `sqlite-json`.
- Capability declarations expose the same key as the provider they describe.

## Capability Declaration

Capability descriptions include provider identity.

```csharp
public sealed record QueryCapabilityDescription(
    string ProviderKey,
    string ProviderName,
    /* existing capability fields */);
```

Expected behavior:

- Validation selects capabilities by matching the active provider key.
- Validation fails closed when no capability declaration matches the active provider key.
- Capability matching does not depend on registration order or type-name convention.

## Query Validation

Preflight validation remains non-throwing.

```csharp
public interface IResourceQueryValidator
{
    QueryValidationResult Validate(ResourceQuery query);
}
```

Expected behavior:

- Supported queries return `IsValid == true`.
- Unsupported queries return structured failures.
- Missing matching capabilities return a `capabilities-not-declared` failure.

## Unsupported Execution Failure

Execution raises structured unsupported-query exceptions when a provider cannot execute a query.

```csharp
public sealed class UnsupportedQueryFeatureException : Exception
{
    public string Code { get; }
    public string Feature { get; }
    public string? Path { get; }
}
```

Expected behavior:

- Code and feature are stable enough for tests and caller handling.
- Message remains actionable for humans.
- When execution fails for a shape validation can detect, code/category should match the validation failure.
- Execution may stop at the first blocking failure.

## Provider Execution Flow

Providers run validation before translation/execution and keep provider-specific checks.

```csharp
public async ValueTask<IEnumerable<Resource>> QueryAsync(
    ResourceQuery query,
    CancellationToken cancellationToken = default)
{
    // Validate with active provider capabilities.
    // Throw structured unsupported execution failure for the first blocking validation failure.
    // Continue with provider-specific translation/execution checks.
}
```

Expected behavior:

- Skipping caller preflight does not bypass unsupported-query safeguards.
- Provider-specific checks remain authoritative.
- Supported query behavior remains unchanged.
