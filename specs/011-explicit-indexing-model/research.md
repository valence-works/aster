# Research: Explicit Indexing Model

## Decision: Extend Query Capabilities With Index Projection Declarations

**Decision**: Add index projection declarations to provider capability descriptions rather than creating a separate provider registry or index registry.

**Rationale**: Query capabilities already communicate provider-specific query support and are resolved by active provider key. Index declarations are another provider capability, so keeping them in the same discovery path is explicit and avoids new registration infrastructure.

**Alternatives considered**:

- Separate index registry: rejected because it creates a second provider-matching path without a demonstrated need.
- Runtime scanning of resource definitions or assemblies: rejected because the constitution and spec require explicit behavior.
- Resource-definition annotations: rejected for this slice by clarification; definitions are not changed in `011`.

## Decision: Built-In Providers Declare Zero Default Projections

**Decision**: In-memory and SQLite JSON providers expose an empty index projection declaration collection in this slice.

**Rationale**: The goal is to establish the contract, not imply physical indexes, planner behavior, or default optimization. Custom/test providers can prove declarations and evaluation without changing built-in provider semantics.

**Alternatives considered**:

- SQLite metadata defaults: rejected because it could imply physical indexes or query-planner behavior that does not exist.
- Common defaults for both built-ins: rejected because it weakens the slice boundary and adds assumptions about consumer indexing needs.

## Decision: Sources Are Metadata Fields Or Aspect/Facet Pairs

**Decision**: Projection sources are limited to resource metadata fields or explicit aspect key plus facet key pairs.

**Rationale**: This matches the existing query vocabulary and keeps declaration/evaluation simple. Nested JSON path support would require path grammar, escaping rules, array semantics, and provider-specific behavior that belongs in a later slice.

**Alternatives considered**:

- Dot-separated nested paths: rejected because object traversal semantics and escaping rules are unclear.
- JSONPath-style source paths: rejected as unnecessary and likely to introduce provider-specific semantics.
- Provider-specific source strings: rejected because the contract would stop being portable.

## Decision: Fail-Soft Projection Evaluation

**Decision**: Projection evaluation returns successful values and structured per-projection failures together.

**Rationale**: Providers need to understand all projection outcomes for a resource version. Throwing on the first incompatible value hides additional valid values/failures, while silent skipping makes data quality issues invisible.

**Alternatives considered**:

- Throw on first incompatible value: rejected because it gives weaker diagnostics and makes batch projection less useful.
- Silently skip incompatible values: rejected because it violates fail-closed/explicit diagnostics expectations.
- Validation only without value evaluation: rejected because the spec requires provider-facing consumption of declarations.

## Decision: Strict Value-Shape Matching

**Decision**: Projection evaluation uses strict shape matching. It does not coerce strings into numeric, boolean, or GUID values. `DateTime` projections may use the existing accepted date/time normalization rules.

**Rationale**: Strict matching keeps behavior predictable and avoids building a culture-sensitive conversion framework. Reusing the existing date/time helper keeps query and indexing semantics aligned where the project already has an accepted portable date/time shape.

**Alternatives considered**:

- Safe string coercion: rejected because "safe" still requires many edge-case decisions and could differ across providers.
- Provider-defined coercion: rejected because it weakens portability for the first contract.
- Culture-aware coercion: rejected as operationally surprising and out of scope.

## Decision: No Storage Or Query Planner Changes

**Decision**: The slice does not add generated columns, physical indexes, migrations, query planning, or execution rewrites.

**Rationale**: The roadmap calls for an explicit indexing model first. Physical storage and planner behavior need separate acceptance criteria once declarations exist.

**Alternatives considered**:

- SQLite generated columns/indexes now: rejected because it adds migration and planner concerns.
- Query rewrite based on declared indexes: rejected because the query engine currently executes directly against provider capabilities.
