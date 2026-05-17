# Research: Typed Query Authoring Ergonomics

## Decision 1: Extend the existing `TypedQuery` helper surface

**Decision**: Add typed sort and logical composition helpers to the existing `TypedQuery` area instead of creating a new query builder framework.

**Rationale**: The current helper already owns typed aspect member selection and convention/override mapping. Extending it keeps the feature discoverable and avoids a second authoring style.

**Alternatives considered**:

- Add a new fluent `ResourceQueryBuilder`. Rejected for this slice because sort and logical helpers can solve the immediate friction with less surface area.
- Add extension methods directly on `ResourceQuery`. Rejected because it would encourage a mutable-builder feel over immutable record construction.
- Use LINQ-style expression predicates. Rejected because it drifts toward an `IQueryable` provider and arbitrary expression translation.

## Decision 2: Typed facet sort helpers emit `SortExpression`

**Decision**: A typed facet selection should be able to produce ascending and descending `SortExpression` values using the same aspect key and facet identifier resolution rules as typed facet filters.

**Rationale**: Facet sorting is now a first-class portable query shape for the built-in providers. Reusing typed facet selection keeps filter and sort authoring consistent.

**Alternatives considered**:

- Sort only through a full `ResourceQuery` builder. Rejected because callers often compose queries manually and should be able to use the smaller part.
- Add typed metadata sort helpers. Rejected for now because metadata fields are not typed aspect members and need a separate design.

## Decision 3: Logical helpers stay small and validate obvious invalid shapes

**Decision**: Add helpers for `And`, `Or`, and `Not` that return `LogicalExpression` and reject empty operand sets or invalid `Not` usage before provider validation.

**Rationale**: The AST already supports logical expressions, but manual construction is noisy and `Not` is easy to shape incorrectly. These helpers improve authoring without changing provider semantics.

**Alternatives considered**:

- Let provider validation catch all invalid logical shapes. Rejected because helper-level argument validation gives faster and clearer feedback.
- Add a rich boolean DSL with operator overloads. Rejected as clever and unnecessary for current requirements.

## Decision 4: Preserve manual construction and provider validation

**Decision**: Helpers are additive. Existing manual `ResourceQuery`, `FilterExpression`, and `SortExpression` construction remains supported, and helper-generated output flows through the same validator and provider execution paths.

**Rationale**: Provider authors should not need to learn a new query pipeline, and advanced users still need direct AST construction.

**Alternatives considered**:

- Make helpers the preferred internal representation. Rejected because the AST is already the provider contract.
- Add provider-specific helper behavior. Rejected because it would fragment query authoring.

## Decision 5: No new dependencies or runtime mapping

**Decision**: Implement the feature with the existing expression member selection logic and explicit per-query overrides. Do not add runtime scanning, global mapping registration, or external expression libraries.

**Rationale**: The current convention plus override model covers the target scenarios. Runtime scanning would obscure behavior and add operational/debugging complexity.

**Alternatives considered**:

- Global typed aspect mapping registry. Deferred until schema metadata and index projection needs prove it out.
- Attribute-based facet mapping. Deferred because it couples query authoring to annotation conventions and does not address named aspect overrides alone.
