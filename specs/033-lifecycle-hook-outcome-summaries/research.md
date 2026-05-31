# Research: Lifecycle Hook Outcome Summaries

## Decision: Support Single and Enumerable Outcome Summaries

**Decision**: Provide `ToSummary()` for a single `LifecycleHookOutcome` and for `IEnumerable<LifecycleHookOutcome>?`.

**Rationale**: Hosts may want to summarize one manually constructed hook outcome or a captured set from tests/logging. A single-outcome helper delegates to the enumerable behavior and keeps the API convenient without adding service or dispatcher concepts.

**Alternatives considered**:

- Enumerable only: rejected because single outcome objects are the primitive result type hosts already construct.
- Dispatcher-integrated summaries: rejected because it would change lifecycle execution behavior and add state to a currently stateless dispatch path.

## Decision: Summarize Existing Outcome and Diagnostic Dimensions Only

**Decision**: Aggregate outcome status, outcome code, nested diagnostic code, diagnostic lifecycle point, and diagnostic hook type.

**Rationale**: These fields already exist on `LifecycleHookOutcome` and `LifecycleHookDiagnostic`. They are useful for host reporting and require no new lifecycle model state.

**Alternatives considered**:

- Add hook invocation ids or timing: rejected because outcomes do not contain this data and adding it would broaden lifecycle execution semantics.
- Persist audit records: rejected because audit persistence requires separate storage and retention decisions.

## Decision: Deterministic Ordering and Blank-Key Filtering

**Decision**: Enum counts sort by enum value. String-key counts use ordinal ordering and omit null/blank keys. Totals still include outcomes and diagnostics with blank keys.

**Rationale**: Deterministic output supports stable tests, logs, dashboards, and host comparisons. Blank filtering follows existing summary patterns for diagnostic and code counts.

**Alternatives considered**:

- Include blank keys as a special bucket: rejected because current summary patterns omit blank keys while preserving totals.
- Preserve input order: rejected because grouped summaries should be stable regardless of source order.

## Decision: Treat Null Outcome Collections and Nested Diagnostics as Empty

**Decision**: Enumerable summary creation treats null outcome collections and null nested diagnostic collections as empty. Single-outcome summary creation throws for a null outcome.

**Rationale**: Existing summary helpers fail fast for missing root result objects but tolerate null nested collections for manually constructed objects. Enumerable summaries follow the project pattern used by schema status summaries.

**Alternatives considered**:

- Throw on null enumerable input: rejected because nullable enumerable summary helpers already treat null collections as empty elsewhere.
- Throw on null nested diagnostics: rejected because manually constructed outcome objects should not make summaries fail.

## Decision: Validate Compatibility with Existing Dispatcher Tests

**Decision**: Add focused summary tests and run existing lifecycle hook dispatcher tests plus full solution validation.

**Rationale**: The slice must not change hook ordering, rejection, failure, or exception behavior. Existing dispatcher tests are the best compatibility guard.

**Alternatives considered**:

- Only run new summary tests: rejected because the feature explicitly promises no dispatcher behavior changes.
