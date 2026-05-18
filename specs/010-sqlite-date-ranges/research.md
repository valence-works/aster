# Research: SQLite Date-Like Facet Ranges

## Decision 1: Support ISO-8601-style string facet values

**Decision**: SQLite date-like facet ranges support JSON string scalar values that can be parsed as `DateTimeOffset` or `DateTime` and normalized to a round-trip UTC string for comparison.

**Rationale**: `System.Text.Json` already emits date/time values as ISO-8601-style strings. Supporting that shape avoids storage changes and keeps behavior compatible with existing persisted payloads.

**Alternatives considered**:

- Store separate numeric ticks or generated columns. Rejected because this slice must not introduce migrations or indexing.
- Support arbitrary local date strings. Rejected because provider behavior would be ambiguous and culture-sensitive.

## Decision 2: Keep numeric and date-like range translation separate

**Decision**: The SQLite translator branches numeric and date-like ranges explicitly.

**Rationale**: Numeric ranges rely on JSON numeric type detection and numeric casts. Date-like ranges rely on string parse/normalization semantics. Keeping the paths separate is clearer and preserves existing numeric behavior.

## Decision 3: Invalid stored values do not match

**Decision**: Stored facet values that are missing, null, non-string, malformed, or not parseable as accepted date/time values do not match date-like range predicates.

**Rationale**: Query results should fail closed at the row level when data does not satisfy the documented shape.

## Decision 4: Invalid query bounds remain structured failures

**Decision**: Query bounds that validation cannot classify as numeric or date-like continue to fail with structured value-shape errors.

**Rationale**: The existing validator already owns bound-shape checks. Execution remains authoritative if validation is bypassed.

## Decision 5: No query planner or indexing model

**Decision**: This slice does not add planner rewrites, generated columns, or index declarations.

**Rationale**: The immediate value is correctness and parity for simple date-like ranges. Indexing belongs to a later explicit indexing slice.
