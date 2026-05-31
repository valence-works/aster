# Research: Query Validation Summaries

## Decision: Use Pure Extension Helper Over Services

**Decision**: Add `ToSummary()` for `QueryValidationResult`.

**Rationale**: Validation summaries are deterministic aggregation over failures already returned by the validator. A service would add DI and lifetime surface without product value.

**Alternatives considered**:

- New summary service: rejected because aggregation is pure and host-local.
- Validator-integrated summary output: rejected because it would widen existing validator contracts.

## Decision: Keep Summary Models in Querying Models

**Decision**: Place records in `src/core/Aster.Core/Models/Querying/QueryValidationSummaries.cs`.

**Rationale**: The summary is part of the query validation model surface and belongs near `QueryValidationResult` and `QueryValidationFailure`.

**Alternatives considered**:

- Add records to `QueryValidationResult.cs`: rejected to keep the existing result contract focused.
- Create a reporting namespace: rejected because it suggests a broader reporting framework.

## Decision: Deterministic Ordinal Count Ordering

**Decision**: Sort code, path, and feature counts using ordinal string ordering.

**Rationale**: Failure codes, paths, and features are stable string identifiers. Ordinal ordering gives predictable output for tests, logs, and host UI.

**Alternatives considered**:

- Preserve input order: rejected because aggregate output should not depend on incidental failure order.
- Culture-sensitive ordering: rejected because failure keys are programmatic identifiers.

## Decision: Ignore Blank Keys in Key-Specific Counts

**Decision**: Ignore null, empty, or whitespace-only codes, paths, and features in their specific count collections while preserving total failure count.

**Rationale**: Total failure count should reflect all failures. Key-specific count lists should only contain actionable buckets.

**Alternatives considered**:

- Throw for blank codes: rejected because manually constructed result objects should not make summaries fail.
- Use an unknown bucket: rejected because the current validation contract uses optional path/feature values and stable nonblank codes.

## Decision: No Query Planner or Capability Expansion

**Decision**: Do not add planner output, provider support analysis, suggested rewrites, or execution changes.

**Rationale**: The slice is host reporting over validation results. Planner/rewrite behavior is a separate product decision and would broaden architecture.

**Alternatives considered**:

- Add remediation hints: rejected because existing failure messages already carry actionable text.
- Add provider capability comparison: rejected because that would require broader provider/capability API design.
