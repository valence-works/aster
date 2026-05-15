# Data Model: Provider Validation Execution Alignment

## Query Provider Key

Identifies the active query provider and links it to its capability declaration.

### Fields

- `Value`: Stable non-empty provider identifier, such as `in-memory` or `sqlite-json`.

### Validation Rules

- Provider key must be non-empty.
- The active query provider and matching capability declaration must use the same key.
- Provider key matching is exact and deterministic.

## Provider Capability Declaration

Extends the existing query capability description with provider identity.

### Fields

- `ProviderKey`: Stable provider key used for matching.
- `ProviderName`: Human-readable provider name.
- Existing capability fields for scopes, filters, comparisons, sorts, paging, value shapes, and exclusions.

### Validation Rules

- `ProviderKey` and `ProviderName` must be non-empty.
- Capability declaration must match provider execution behavior.
- If no declaration matches the active provider key, validation fails closed.

## Query Validation Failure

Existing preflight failure result for unsupported query features.

### Fields

- `Code`: Stable failure code.
- `Feature`: Unsupported feature category.
- `Message`: Actionable explanation.
- `Path`: Optional query location.

### Validation Rules

- Code and message must be non-empty.
- Feature should be present for unsupported provider capabilities.
- Paths should be included when failures map to a specific predicate, sort, scope, or paging property.

## Unsupported Query Execution Failure

Execution-time failure for unsupported query shapes.

### Fields

- `Code`: Stable failure code aligned with validation when applicable.
- `Feature`: Unsupported feature category.
- `Message`: Actionable explanation.
- `Path`: Optional query location.

### Validation Rules

- Code, feature, and message must be available to callers.
- Execution may report the first blocking unsupported feature.
- Provider-specific failures should use the same code/category as validation when both paths detect the same shape.

## Provider Consistency Case

Test scenario that compares validation and execution behavior.

### Fields

- `ProviderKey`: Provider under test.
- `QueryShape`: Query scenario being validated and executed.
- `ExpectedValidation`: Expected validation result category/code.
- `ExpectedExecution`: Expected execution failure category/code or success.

### Validation Rules

- Each provider must include at least one supported query consistency case.
- Each provider must include unsupported cases that cover provider-specific exclusions.
- Cases must catch both validation-accepts/execution-rejects and validation-rejects/execution-supports drift.

## State Transitions

### Query Execution

```text
ResourceQuery
  ├─ shared validation succeeds
  │   ├─ provider translation/execution succeeds → results
  │   └─ provider-specific check fails → unsupported execution failure
  └─ shared validation fails → unsupported execution failure from first blocking validation failure
```

### Capability Matching

```text
Active query provider key
  ├─ matching capability declaration exists → validate with matched capabilities
  └─ no matching declaration exists → capabilities-not-declared validation failure
```
