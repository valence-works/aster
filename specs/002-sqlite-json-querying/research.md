# Research: SQLite JSON Querying (Phase 2A)

## Decision: Provider-specific query services own SQL translation

**Decision**: Implement `SqliteJsonQueryService` inside `Aster.Persistence.SqliteJson` rather than adding a generic SQL generator or `IQueryable` provider.

**Rationale**: Aster query semantics include resource version scopes, activation channels, JSON aspect payloads, and provider-specific JSON behavior. The hard part is not generic SQL generation; it is preserving portable Aster semantics for each provider. Keeping translation provider-specific is simpler and more explicit.

**Alternatives considered**:

- **Generic SQL builder package**: Helpful for SQL string composition, but does not solve JSON path semantics, activation scope semantics, or provider capability decisions. Reconsider if internal SQL helper code grows beyond the current subset.
- **EF Core**: Adds an ORM model that does not match Aster's provider-defined snapshot/JSON storage shape. It would likely require raw SQL for the interesting JSON queries anyway.
- **Dapper**: Useful row mapping/parameter helper, but not necessary while `Microsoft.Data.Sqlite` is already sufficient and dependency minimization is preferred.
- **Public `IQueryable` provider**: Rejected. It would imply broad LINQ support and provider behavior leakage. Future typed query helpers should compile a small expression subset into `ResourceQuery` instead.

## Decision: Use `Microsoft.Data.Sqlite` directly for Phase 2A

**Decision**: Continue using `Microsoft.Data.Sqlite` directly for query execution.

**Rationale**: It is already a provider dependency, supports parameterized commands, and keeps operational/dependency complexity low.

**Alternatives considered**:

- **SqlKata**: A reasonable future option if SQL composition becomes noisy. Not needed for the first provider-specific translator.
- **Dapper**: Not needed for payload deserialization and simple row reading currently handled directly.

## Decision: SQLite JSON lookup uses provider-owned JSON path helper

**Decision**: Add a small helper to create SQLite JSON paths for aspect keys and facet identifiers.

**Rationale**: JSON paths are not ordinary SQL parameters in SQLite expressions, so the provider must avoid unsafe path interpolation. The helper should validate or safely quote path segments before SQL assembly.

**Source context**: SQLite JSON functions are built into SQLite by default as of SQLite 3.38.0, and `json_extract()` returns SQL scalar values for single-path scalar lookups. See SQLite JSON functions documentation: https://www.sqlite.org/json1.html

## Decision: Unsupported means typed failure, not fallback

**Decision**: Unsupported SQLite query shapes throw `UnsupportedQueryFeatureException`.

**Rationale**: Silent in-memory fallback hides performance and semantic differences. Explicit failure matches the provider honesty goal and keeps future capability negotiation possible.

## Decision: Typed LINQ-like helpers are future AST authoring, not this feature

**Decision**: Do not implement typed LINQ-like query builders in this feature. Future helpers may parse a small subset of `Expression<Func<TAspect, bool>>`, but must emit `ResourceQuery`.

**Rationale**: SQLite query execution correctness should come before authoring ergonomics. Keeping the AST as the contract avoids the `IQueryable` trap and keeps provider behavior explicit.
