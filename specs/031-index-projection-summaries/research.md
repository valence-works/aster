# Research: Index Projection Summaries

## Decision: Use Pure Extension Helpers Over Services

**Decision**: Add `ToSummary()` helpers for `IndexProjectionValidationResult` and `IndexProjectionEvaluationResult`.

**Rationale**: Projection summaries are deterministic aggregation over result objects already produced by validators/evaluators. A service would add DI and lifetime surface without current product value.

**Alternatives considered**:

- New summary service: rejected because aggregation is pure and host-local.
- Validator/evaluator-integrated summary output: rejected because it widens existing contracts.

## Decision: Keep Summary Models in Querying Models

**Decision**: Place records in `src/core/Aster.Core/Models/Querying/IndexProjectionSummaries.cs`.

**Rationale**: Projection validation/evaluation are query-model concepts, and prior summary slices keep host-facing summaries near the result types they summarize.

**Alternatives considered**:

- Add records to existing projection files: rejected to keep core result contracts focused.
- Create a reporting namespace: rejected because it suggests a broader reporting framework.

## Decision: Deterministic Count Ordering

**Decision**: Sort string-keyed counts using ordinal string ordering and enum-keyed field-type counts by enum value.

**Rationale**: Projection field names, sources, and failure codes are programmatic identifiers. Deterministic ordering keeps tests, logs, and host UI stable.

**Alternatives considered**:

- Preserve input order: rejected because aggregate output should not depend on incidental result order.
- Culture-sensitive string ordering: rejected because keys are not user-language text.

## Decision: Ignore Blank Failure Keys in Key-Specific Counts

**Decision**: Ignore null, empty, or whitespace-only failure codes, field names, and sources in their specific count collections while preserving total failure count.

**Rationale**: Total failure count should reflect all failures. Key-specific buckets should only contain actionable identifiers.

**Alternatives considered**:

- Throw for blank keys: rejected because manually constructed result objects should not make summaries fail.
- Use unknown buckets: rejected because current projection failure contracts already model optional field/source values.

## Decision: Keep Physical Indexing and Planning Out of Scope

**Decision**: Do not add physical index metadata, provider setup checks, planner output, or query execution integration.

**Rationale**: The slice summarizes result objects only. Physical indexing and query planning are separate product decisions.

**Alternatives considered**:

- Add physical index readiness reporting: rejected because the project currently has only explicit projection declarations, not physical index infrastructure.
- Add query planner diagnostics: rejected because it broadens the provider/query architecture.
