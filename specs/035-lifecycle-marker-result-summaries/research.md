# Research: Lifecycle Marker Result Summaries

## Decision: Support Single and Enumerable Marker Result Summaries

**Decision**: Provide `ToSummary()` for a single `ResourceLifecycleMarkerResult` and for `IEnumerable<ResourceLifecycleMarkerResult>?`.

**Rationale**: Hosts may report one marker write or a manually collected set of marker write results. A single-result helper delegates to the enumerable behavior and keeps the API convenient without adding batch marker application behavior.

**Alternatives considered**:

- Enumerable only: rejected because the marker service returns a single result object.
- Batch marker service: rejected because this slice is reporting-only and does not introduce workflow infrastructure.

## Decision: Summarize Existing Marker and Diagnostic Dimensions Only

**Decision**: Aggregate success/failure, marker presence, marker state, marker resource identifier, diagnostic code, diagnostic path, and diagnostic resource identifier.

**Rationale**: These fields already exist on `ResourceLifecycleMarkerResult`, `ResourceLifecycleMarker`, and `ResourcePolicyDiagnostic`. They are useful for host reporting and require no new marker service or policy model state.

**Alternatives considered**:

- Add operation ids, durations, or audit timestamps: rejected because result objects do not contain this data and adding it would broaden marker write semantics.
- Persist reporting records: rejected because audit/report persistence requires separate storage and retention decisions.

## Decision: Reuse Policy Diagnostic Code Counts

**Decision**: Reuse `ResourcePolicyDiagnosticCodeCount` and `ResourcePolicyDiagnosticCodeCounter` for diagnostic code counts.

**Rationale**: Marker result diagnostics use the policy diagnostic model. Reuse keeps diagnostic code semantics aligned with existing policy, lifecycle restore, and policy application summaries.

**Alternatives considered**:

- Marker-specific diagnostic code count record: rejected because it would duplicate an existing diagnostic shape without adding behavior.

## Decision: Deterministic Ordering and Blank-Key Filtering

**Decision**: Enum counts sort by enum value. String-key counts use ordinal ordering and omit null/blank keys. Totals still include results and diagnostics with blank keys.

**Rationale**: Deterministic output supports stable tests, logs, dashboards, and host comparisons. Blank filtering follows existing summary patterns for diagnostic and resource counts.

**Alternatives considered**:

- Include blank keys as a special bucket: rejected because current summary patterns omit blank keys while preserving totals.
- Preserve input order: rejected because grouped summaries should be stable regardless of source order.

## Decision: Treat Null Collections and Entries as Empty

**Decision**: Enumerable summary creation treats null result collections as empty and ignores null result entries. Single-result summary creation throws for a null result. Null nested diagnostics are treated as empty.

**Rationale**: Existing summary helpers fail fast for missing root result objects but tolerate null nested collections for manually constructed objects. Enumerable summaries follow existing null-as-empty patterns.

**Alternatives considered**:

- Throw on null enumerable input: rejected because nullable enumerable summary helpers already treat null collections as empty elsewhere.
- Throw on null nested diagnostics: rejected because manually constructed marker result objects should not make summaries fail.

## Decision: Validate Compatibility with Existing Marker Service Tests

**Decision**: Add focused summary tests and run existing lifecycle marker service/conflict tests plus full solution validation.

**Rationale**: The slice must not change marker writes, conflicts, target-not-found diagnostics, in-memory behavior, or SQLite behavior. Existing marker tests are the best compatibility guard.

**Alternatives considered**:

- Only run new summary tests: rejected because the feature explicitly promises no marker behavior changes.
