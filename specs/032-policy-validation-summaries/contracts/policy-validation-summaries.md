# Contract: Policy Validation Summaries

## Public SDK Behavior

Policy validation results expose an explicit summary helper:

```csharp
ResourcePolicyValidationSummary ToSummary(this ResourcePolicyValidationResult result)
```

The helper MUST:

- Throw `ArgumentNullException` when `result` is null.
- Treat `result.Diagnostics` as empty when the collection is null.
- Return a pure in-memory summary without service resolution, provider access, storage access, policy evaluation, or mutation.
- Preserve current policy validation diagnostics and behavior unchanged.

## Summary Contract

`ResourcePolicyValidationSummary` MUST expose:

- `TotalDiagnosticCount`
- `IsValid`
- `HasDiagnostics`
- `DiagnosticCodeCounts`
- `DiagnosticPathCounts`
- `PolicyIdCounts`
- `ResourceIdCounts`
- `ResourceVersionCounts`

`IsValid` MUST be true when `TotalDiagnosticCount` is zero.

`HasDiagnostics` MUST be true when `TotalDiagnosticCount` is greater than zero.

## Count Contracts

Diagnostic code counts use the existing `ResourcePolicyDiagnosticCodeCount` contract.

Additional policy validation count records MUST expose:

- `ResourcePolicyDiagnosticPathCount`: `Path`, `Count`
- `ResourcePolicyDiagnosticPolicyIdCount`: `PolicyId`, `Count`
- `ResourcePolicyDiagnosticResourceIdCount`: `ResourceId`, `Count`
- `ResourcePolicyDiagnosticResourceVersionCount`: `ResourceVersion`, `Count`

String-key counts MUST omit null, empty, or whitespace-only keys and sort remaining keys using ordinal comparison.

Resource-version counts MUST omit diagnostics without a resource version and sort by resource version ascending.

## Non-Goals

This feature MUST NOT introduce:

- Storage schema changes
- Provider behavior changes
- Service registration changes
- Runtime scanning or automatic discovery
- Schedulers
- Audit persistence
- Policy validation rule changes
- Policy execution changes
- Public raw SQL
- Public `IQueryable<Resource>`
- Query planner behavior
- Resource, version, lifecycle marker, definition, or activation mutation
