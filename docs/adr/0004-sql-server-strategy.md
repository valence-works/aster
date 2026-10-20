# ADR 0004: SQL Server support strategy

- **Status:** Accepted
- **Date:** 2026-10-20
- **Program task:** [#102](https://github.com/valence-works/aster/issues/102) (epic [#65](https://github.com/valence-works/aster/issues/65))
- **Inputs:** Elsa consumer spike ([#78](https://github.com/valence-works/aster/issues/78), `docs/consumers/elsa-spike.md`), ADR 0001 (control-plane boundary), ADR 0002 §9 (definition-agnostic queries)

## Context

Aster ships an in-memory provider and a SQLite JSON provider. A PostgreSQL JSONB provider is planned (#97, #98, #100).

The Elsa spike made SQL Server a gating requirement: Elsa hosts run SQL Server in production, and the Elsa module epic (#69) is gated on a production provider they can use. Elsa users also run Postgres, MySQL, SQLite and MongoDB, mostly through EF Core.

What a provider has to do is narrow but exacting:

| Concern | What it needs |
|---|---|
| Store primitives | Append-only version rows, activation state, lifecycle markers, definition and aspect versions, atomic portability import, bounded reads by resource-ID set. These are simple keyed SQL. |
| Portable queries | A `ResourceQuery` AST translated over **arbitrary JSON paths** that no CLR type describes, because queries are definition-agnostic (ADR 0002 §9). Operators: `Equals` / `NotEquals` / `In` (case-insensitive text), `Contains` / `StartsWith`, numeric and date `Range`, `Exists`, and the array operators `ArrayContains` / `Any` / `All`. |
| Indexes | Physical indexes derived from declared index projections (#100). |
| Startup | Idempotent schema bootstrap that is safe under concurrent startup (036/037, #99). |

The SQLite provider already holds the hard part: a dialect-specific JSON predicate translator (`SqliteWhereTranslator`, `SqliteFacetValueExpression`, `SqliteJsonPath`). Its store primitives are a few hundred lines of plain SQL.

## Options

### A. Native SQL Server provider

- **How it queries:** `JSON_VALUE` / `OPENJSON` translation for the portable AST.
- **How it indexes:** persisted computed columns over `JSON_VALUE` for declared projections, with ordinary indexes on them.
- **Connectivity:** `Microsoft.Data.SqlClient` only.

### B. Generic EF Core provider with per-database JSON translators

- **How it would work:** one EF Core model of the version, definition, activation and marker tables, plus a per-database JSON predicate translator.
- **What it would cover:** SQL Server, Postgres, MySQL and SQLite through one code path.

### C. Defer

Elsa uses Postgres or SQLite at first; SQL Server comes later.

## Decision

**Option A: a native SQL Server provider, built after the Postgres provider, on a shared relational core that is extracted at that point.** C is rejected; B is rejected for now.

### Why not B (EF Core)

- **EF Core doesn't remove the hard part.** EF's JSON support maps JSON columns to *known CLR types* (owned and complex types). Aster's queries target arbitrary JSON paths chosen at runtime by definitions EF has never seen. Every predicate would still be hand-written SQL fragments per database (`JSON_VALUE` vs `->>`/`jsonb_path_exists` vs `json_extract`), injected through `FromSql` or custom function translations. The per-database translator remains; EF only adds a layer around it.
- **What EF would replace is the easy part.** It would handle the store primitives (keyed inserts and reads) and migrations, which are small. In exchange, every consumer would take an EF Core dependency, with its version coupling to the host's EF version. Elsa pins EF versions per release, and Aster must not constrain them.
- **Index derivation is per-database anyway.** Computed columns on SQL Server, expression indexes on Postgres, and generated columns on SQLite all need dialect DDL, and EF migrations would model them as raw SQL.
- **Conformance is per-database regardless.** Every database must pass the shared conformance suite, and case-insensitive text semantics differ by collation. B saves no test effort.

### Why not C (defer)

The spike gates the Elsa module (#69) on a production provider that Elsa's SQL Server hosts can use. Deferring would either block #69 or make Aster's first real consumer run a second database just for Aster.

### Shape of the decision

1. **Postgres first (#97, #98, #100), unchanged.** It is the second real SQL dialect and the reference for index projections.
2. **Extract a relational core when Postgres lands.** That is the third implementation, so the shared seams are visible rather than guessed. The core holds:
   - the portable-AST walker
   - the parameter bag
   - paging and sorting assembly
   - bounded-ID batching
   - the schema-bootstrap pattern (idempotent DDL plus a per-database startup lock, #99)

   It is driven by an internal `ISqlJsonDialect`, which supplies:
   - JSON path extraction
   - case-insensitive text comparison
   - numeric and date casts
   - array element enumeration
   - existence checks
   - DDL for tables and projection indexes
3. **SQL Server provider (`Aster.Persistence.SqlServerJson`) on that core.**
   - **JSON storage:** payloads in `nvarchar(max)`, read with `JSON_VALUE` / `OPENJSON`. The native `json` type is not required: it isn't available on SQL Server 2019/2022, which Elsa hosts run.
   - **Index projections:** persisted computed columns plus indexes.
   - **Text semantics:** case-insensitive comparison through an explicit `COLLATE` in predicates, so results don't depend on the database's default collation.
   - **Conformance:** it passes the shared conformance suite, like every provider.
4. **Not planned: MySQL or MongoDB.** The dialect seam makes MySQL a contained follow-up if a consumer asks. MongoDB is not relational and would be a separate ADR.

### Rejected alternative within A

**Building SQL Server before Postgres.** Postgres is the stronger default for new deployments, and its JSONB operators and expression indexes exercise the index-projection design (#100) most directly. Two dialects plus SQLite is the right moment to extract the core. Elsa can develop against Postgres or SQLite while SQL Server follows.

## Consequences

- **Follow-up:** #152 (SQL Server provider on the shared relational core) under epic #65, dependent on #98, #99, #100 and #101.
- **Elsa (#69):** module development proceeds against SQLite or Postgres. The first production entity move (#118) for SQL Server hosts waits on the SQL Server provider.
- **Dependency footprint:** the core SDK stays EF-free. Each provider package depends only on its ADO.NET driver.
- **Cost:** three dialects to maintain (SQLite, Postgres, SQL Server). The relational core and the shared conformance suite keep dialect code to the translator and DDL.
- **Revisit trigger:** if two or more further relational databases are requested, or if EF Core gains dynamic JSON-path query translation across providers, re-evaluate option B.
