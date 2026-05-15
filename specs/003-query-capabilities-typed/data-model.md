# Data Model: Query Capabilities & Typed Query Helpers

## Query Capability Description

Describes what a query provider can execute.

### Fields

- `ProviderName`: Human-readable provider identifier.
- `SupportedScopes`: Set of supported resource version scopes.
- `RequiresActivationChannelForActiveScope`: Whether active scope requires a channel.
- `SupportedFilterTypes`: Set of supported filter expression categories.
- `SupportedLogicalOperators`: Set of supported logical operators.
- `SupportedComparisonOperators`: Set of supported comparison operators by filter category.
- `SupportedMetadataFields`: Set of metadata fields accepted for filtering and/or sorting.
- `SupportsMetadataSorting`: Whether metadata sort expressions are supported.
- `SupportsFacetSorting`: Whether facet sort expressions are supported.
- `SupportsSkip`: Whether skip paging is supported.
- `SupportsTake`: Whether take paging is supported.
- `FacetRangeSupport`: Supported facet range value shapes, initially numeric scalar for SQLite JSON and numeric/date-like comparison for in-memory behavior.
- `UnsupportedFeatures`: Human-readable exclusions for discoverability and documentation.

### Validation Rules

- Provider name must be non-empty.
- Supported collections must be empty rather than null.
- Missing provider capabilities are treated as validation failure by consumers.
- SQLite JSON capabilities must declare facet sorting unsupported.
- Capability declarations must match provider execution behavior.

## Query Validation Result

Represents the result of validating a resource query against a provider's capabilities.

### Fields

- `IsValid`: True when no validation failures exist.
- `Failures`: Collection of query validation failures.

### Validation Rules

- `IsValid` must be equivalent to `Failures.Count == 0`.
- Validation must not mutate the query being validated.
- Validation should include all detectable failures where practical.

## Query Validation Failure

Represents one unsupported or invalid query shape.

### Fields

- `Code`: Stable failure code suitable for tests and documentation.
- `Message`: Human-readable actionable explanation.
- `Path`: Optional location within the query shape, such as `Filter.Operands[1]` or `Sorts[0]`.
- `Feature`: The unsupported feature category, such as scope, metadata field, facet sort, comparison operator, value shape, or capabilities missing.

### Validation Rules

- Code and message must be non-empty.
- Message must identify what is unsupported and, where possible, the active provider.
- Path should be included when the failure can be tied to a specific predicate or sort.

## Typed Query Mapping

Represents how typed helper input maps to portable query identifiers.

### Fields

- `AspectType`: Typed aspect CLR type.
- `MemberName`: Selected typed aspect member name.
- `AspectKey`: Generated or overridden aspect key.
- `FacetIdentifier`: Generated or overridden facet identifier.

### Validation Rules

- Default `AspectKey` is the typed aspect CLR type name.
- Default `FacetIdentifier` is the selected member name.
- Per-query overrides may replace aspect key and/or facet identifier.
- Aspect key and facet identifier must be non-empty after convention and overrides are applied.
- Member selection must identify a single readable member.

## Typed Query Helper Output

The portable query object produced by typed helper construction.

### Forms

- `AspectPresenceFilter` for typed aspect presence.
- `FacetValueFilter` for typed facet comparisons.
- `ResourceQuery` when helper composition creates a full query.

### Validation Rules

- Output must be inspectable as existing query model records.
- Output must not depend on provider-specific state.
- Output must be accepted by the shared validator like any manually built query.

## State Transitions

### Query Validation Result

```text
Unvalidated ResourceQuery
  ├─ validate with declared capabilities and no failures → Valid result
  ├─ validate with declared capabilities and failures → Invalid result with failures
  └─ validate without declared capabilities → Invalid result with capabilities-not-declared failure
```

### Typed Helper Construction

```text
Typed aspect + member selector
  ├─ apply default convention
  ├─ apply per-query overrides, if any
  └─ emit portable query/filter record
```
