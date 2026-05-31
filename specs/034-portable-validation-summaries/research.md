# Research: Portable Validation Summaries

## Decision: Extend Existing Portability Result Summaries

**Decision**: Add `PortableValidationSummary` and `ToSummary(this PortableSnapshotValidationResult result)` to `PortableResultSummaries.cs`.

**Rationale**: Export, import preview, and import result summaries already live in this file and already define reusable portability diagnostic severity/code count records. Keeping validation there avoids another small file and keeps the public summary surface discoverable.

**Alternatives considered**:

- Separate `PortableValidationSummaries.cs`: rejected because the new summary shares the same diagnostic aggregation helpers and count entities as existing portability result summaries.
- Service-based reporting: rejected because hosts only need a pure projection over an already available result object.

## Decision: Preserve the Source Validity Boolean

**Decision**: The summary records `IsValid` directly from `PortableSnapshotValidationResult.IsValid`.

**Rationale**: The validation result is the source of truth. Recomputing validity from diagnostics could diverge if future validation semantics include non-error invalid states or explicit overrides.

**Alternatives considered**:

- Derive validity from absence of error diagnostics: rejected because the result already exposes the explicit validation decision.

## Decision: Reuse Existing Diagnostic Count Records

**Decision**: Reuse `PortableDiagnosticSeverityCount` and `PortableDiagnosticCodeCount` for validation summaries.

**Rationale**: The same diagnostic model is used by export, preview, import, and validation results. Reuse keeps the SDK shape consistent and avoids duplicated count records.

**Alternatives considered**:

- Validation-specific count records: rejected because they would add names without adding behavior or clarity.

## Decision: Deterministic Ordering and Blank-Code Filtering

**Decision**: Severity counts sort by enum value. Code counts use ordinal string ordering and omit null/blank codes. Total diagnostic count still includes diagnostics with blank codes.

**Rationale**: This matches existing portability result summary behavior and keeps host tests, logs, and dashboards stable.

**Alternatives considered**:

- Include blank codes as a special bucket: rejected because existing portability summaries omit blank keys while preserving totals and severity counts.
- Preserve input order: rejected because grouped summaries should be stable regardless of diagnostic order.

## Decision: Treat Null Nested Diagnostics as Empty

**Decision**: Summary creation throws for a null root `PortableSnapshotValidationResult` and treats null `Diagnostics` as empty.

**Rationale**: Existing single-result summary helpers fail fast for missing root result objects but tolerate null nested collections for manually constructed results.

**Alternatives considered**:

- Throw on null diagnostics: rejected because manually constructed result objects should not make reporting helpers fragile.

## Decision: Validate Compatibility with Existing Portability Tests

**Decision**: Add focused validation summary tests and run existing portability result summary tests, portability validation tests, full solution tests, and build.

**Rationale**: The slice promises no validation service, import/export, provider, or storage behavior changes. Existing tests are the best compatibility guard.

**Alternatives considered**:

- Only run new summary tests: rejected because the feature explicitly preserves existing portability behavior.
