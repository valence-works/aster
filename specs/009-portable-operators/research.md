# Research: Portable Operator Expansion

## Decision 1: Extend `ComparisonOperator`

**Decision**: Add `NotEquals`, `In`, `StartsWith`, and `Exists` to the existing comparison operator enum.

**Rationale**: The current AST already routes metadata and facet comparisons through this enum. Extending it is simpler and more explicit than adding new filter records.

**Alternatives considered**:

- Add a dedicated `FacetExistsFilter`. Rejected for this slice because `FacetValueFilter` already addresses an aspect/facet pair and `Exists` can be represented as an operator.
- Add provider-specific operators. Rejected because the goal is portable capability-discoverable query shapes.

## Decision 2: Treat `Exists` as facet-only support

**Decision**: Built-in providers support `Exists` for facet filters, not metadata filters.

**Rationale**: Metadata fields already have a known schema; the missing gap is determining whether a facet is present inside an aspect.

## Decision 3: Validate `In` value shape before execution

**Decision**: `In` requires a non-string enumerable with at least one candidate.

**Rationale**: Strings are enumerable but should be treated as scalar values. Empty candidate sets are usually caller mistakes and can be rejected consistently before provider execution.

## Decision 4: Prefix matching follows existing text semantics

**Decision**: `StartsWith` uses case-insensitive text comparison, matching existing `Contains` and `Equals` string behavior.

**Rationale**: Query text semantics should remain unsurprising and provider-neutral.

## Decision 5: SQLite translates only explicit/simple shapes

**Decision**: SQLite JSON supports the new operators using existing metadata columns, JSON facet extraction, and registered text functions.

**Rationale**: The operators can be translated directly without introducing indexes, SQL exposure, or a query planner.
