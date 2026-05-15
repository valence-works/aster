# Research: Query Capabilities & Typed Query Helpers

## Decision 1: Represent provider support as explicit immutable capability descriptions

**Decision**: Add a provider-owned query capability description that lists supported scopes, filters, logical operators, comparison operators, sort categories, paging support, supported metadata fields, and provider-specific exclusions.

**Rationale**: The current in-memory and SQLite JSON providers intentionally support different query subsets. A single immutable description makes those differences discoverable without executing a query and keeps provider support explicit.

**Alternatives considered**:

- Infer capabilities by probing providers with sample queries. Rejected because it is brittle, slow, and operationally surprising.
- Encode capabilities only in documentation. Rejected because validation and tests need machine-readable provider behavior.
- Use a free-form dictionary. Rejected because it would be harder to validate, document, and evolve safely.

## Decision 2: Fail closed when a provider has no declared capabilities

**Decision**: Query validation returns an invalid structured result with a "capabilities not declared" failure when the active provider lacks a capability description.

**Rationale**: Unknown provider support should not be treated as full support. Failing closed aligns with explicitness and avoids accidental reliance on fallback behavior.

**Alternatives considered**:

- Allow execution to decide. Rejected because it weakens preflight and makes validation unreliable.
- Assume the smallest portable baseline. Rejected because the baseline is still provider-specific in practice.

## Decision 3: Use a shared validator that returns structured results

**Decision**: Add a shared query validator that consumes a `ResourceQuery` plus capabilities and returns a structured validation result containing `IsValid` and all detectable failures.

**Rationale**: Validation is expected product behavior, not an exceptional control path. A structured result supports UI/reporting, tests, and multiple failures in one pass.

**Alternatives considered**:

- Throw for preflight validation. Rejected because preflight failures are expected user feedback.
- Return only the first failure. Rejected because the spec requires all detectable failures where practical.
- Duplicate validation inside each provider. Rejected because common query AST traversal would drift.

## Decision 4: Execution still enforces unsupported shapes

**Decision**: Provider query services continue to reject unsupported query shapes explicitly during execution. They may reuse the shared validator, but execution failures remain typed/actionable and no silent fallback is introduced.

**Rationale**: Preflight validation is advisory and may be skipped. Execution must remain safe and consistent.

**Alternatives considered**:

- Make validation mandatory before execution. Rejected because it complicates existing call sites and duplicates work for known-supported queries.
- Fall back to in-memory execution. Rejected because it violates provider semantics and can cause large hidden materialization.

## Decision 5: Typed helpers compile to the existing `ResourceQuery` AST

**Decision**: Typed query helpers create `ResourceQuery` or `FilterExpression` records and never introduce a public query provider abstraction.

**Rationale**: This preserves provider portability and the existing query contract while reducing stringly-typed query construction.

**Alternatives considered**:

- Public `IQueryable<Resource>`. Rejected by constitution and prior design decision.
- Provider-specific typed helper APIs. Rejected because they would fragment query construction.
- Full expression-to-query translation. Rejected as too broad for this slice.

## Decision 6: Typed helper mapping uses convention with per-query overrides

**Decision**: By default, aspect key is the typed aspect CLR type name and facet identifier is the selected member name. Helpers also allow explicit per-query overrides for aspect key and/or facet identifier.

**Rationale**: The convention matches existing examples and keeps common cases concise. Overrides support named aspects and non-conventional facet identifiers without global mapping infrastructure.

**Alternatives considered**:

- Convention only. Rejected because named aspects and legacy identifiers would be awkward.
- Global registration-time mapping. Rejected as a larger mapping framework better deferred until schema metadata matures.
- Require explicit aspect key and facet identifier every time. Rejected because it does not meaningfully reduce stringly-typed usage.

## Decision 7: Expression handling is limited to member selection

**Decision**: Typed helper APIs may accept member selectors only to identify a single typed aspect member. They do not parse arbitrary predicates or translate expression bodies.

**Rationale**: Member selection is sufficient to get type/member names safely. General expression translation would resemble a LINQ provider and introduce complexity this feature explicitly avoids.

**Alternatives considered**:

- Parse arbitrary boolean expressions. Rejected as premature and close to an `IQueryable` provider.
- Require member names as strings. Rejected because it fails the typed-helper ergonomics goal.

## Decision 8: No new third-party dependencies

**Decision**: Implement capability declarations, validation, and typed helper construction with platform capabilities and existing project dependencies.

**Rationale**: The feature is mostly records, AST traversal, and member metadata. Additional packages would not materially reduce risk.

**Alternatives considered**:

- Add expression parsing or mapper libraries. Rejected because current requirements need only simple member selection and explicit overrides.
