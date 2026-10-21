# ADR 0006: SQL Server projection indexes and text comparison

- **Status:** Accepted
- **Date:** 2026-10-21
- **Supersedes:** two parts of ADR 0004 §3: "Projections are materialized, not computed" (with its key-size rule) and "Text semantics". The rest of ADR 0004 stands.
- **Program task:** [#152](https://github.com/valence-works/aster/issues/152) (epic [#65](https://github.com/valence-works/aster/issues/65))

## Context

ADR 0004 chose a native SQL Server provider on the shared relational core and fixed two details in advance.

- **Projections.** Declared index projections would be materialized at write time into a `projection_values` side table. The ADR rejected computed columns over `JSON_VALUE` for two reasons:
  - `JSON_VALUE` returns NULL for strings over 4,000 characters, so long values would be neither indexed nor matched.
  - It returns NULL for arrays, so it can't produce one key per element.
- **Text.** Every text comparison would run under one collation, `Latin1_General_100_CI_AS_SC`.

Building the provider (#152) showed that both details are worse than an alternative.

- **The side table costs the most where the core is shared.**
  - Writing scalar projections in the same transaction as each version insert changes the shared store's write path for one dialect.
  - Every projection change then needs a backfill that the provider writes and maintains itself.
- **The objections to computed columns can be met precisely.**
  - Scalar text projections are only used for `Equals` and `In`. Equal strings have equal lengths, so a stored value over 4,000 characters can never equal a query value of 4,000 characters or fewer.
  - Arrays don't need computed columns at all. They need per-element rows, which a computed column can't hold but a separate table can.
- **The collation doesn't give ordinal case-insensitive semantics.** The other providers use `StringComparison.OrdinalIgnoreCase`. A case-insensitive collation behaves differently in three ways:
  - it equates `ß` with `ss`;
  - it ignores some characters;
  - `=` ignores trailing spaces.

  So the same query would return different results on SQL Server than on Postgres and SQLite, which the shared conformance suite forbids.

## Decision

### 1. Scalar projections are persisted computed columns

- **Scalar facets** get a `PERSISTED` computed column on `resource_versions`, read with `JSON_VALUE`. The column holds the projection's key, and has an index on `(tenant_id, column)`. The key depends on the facet type:
  - a number or a date key, for numeric and date facets;
  - for text facets, the upper-cased first 400 characters, 800 bytes, so the index stays under the 1,700-byte key limit.
- **Text metadata** gets a computed column over the metadata column, indexed the same way.
- **`Version` and `Created`** get an index on the existing column.
- **SQL Server backfills computed columns itself** when they are added. There is no provider-maintained backfill and no extra write on save.

### 2. Array projections are element rows

- **Storage:** `KeywordArray` projections get one row per string element in `aster_projection_elements`, keyed by the same upper-cased 400-character prefix.
- **Writes:** an `AFTER INSERT` trigger on `resource_versions` writes the rows.
- **Backfill:** the infrastructure runner backfills when a projection is declared.
- **Deletes:** `ON DELETE CASCADE` removes the rows together with their version, including when pruning.

### 3. Index conjuncts only narrow, never decide

A predicate over a projected field gets an extra conjunct over the indexed column or the element rows, so the optimizer can seek the index. The original predicate is always kept, so the conjunct changes which rows are read, never which rows are returned.

A conjunct is added only when the predicate implies it:
- **Text:** only `Equals` and `In`, and only for values of 4,000 characters or fewer, for the length reason above.
- **Numbers:** equality, `In` and ranges.
- **Dates:** ranges.
- **Arrays:** `ArrayContains`, `ArrayContainsAll` and `ArrayContainsAny` with text candidates.

### 4. Text comparison is ordinal ignore-case

Every text predicate, indexed or not, compares two transformed sides: both upper-cased under `Latin1_General_100_CI_AS_SC` (the collation `UPPER` uses for its case mapping), then compared under `Latin1_General_100_BIN2`.

- Equality also compares `DATALENGTH`, so trailing spaces count.
- Ordering sorts the upper-cased text by code point.
- The result matches `OrdinalIgnoreCase` on the other providers. Like ADR 0004's rule, it doesn't depend on the database's default collation.
- Identifier columns (tenant, resource, definition, channel) are declared `BIN2`, so they compare and sort ordinally as in the other providers.

### Rejected alternatives

- **The `projection_values` side table (ADR 0004).** It needs a second write path in the shared store and a provider-written backfill. It buys nothing that the computed columns and element rows above don't already cover.
- **Comparing under the case-insensitive collation (ADR 0004).** Its results differ from the other providers on `ß`/`ss`, ignorable characters and trailing spaces.
- **Computed columns over `OPENJSON`.** Computed columns can't use table-valued functions.

## Consequences

- **Cost of long values.** A text facet value over 4,000 characters has a NULL computed key. It stays fully queryable through the unindexed `OPENJSON` path, but equality on it can't use the index. Values that long are unusual for indexed keyword facets.
- **JSON key escaping.** Computed columns must use JSON paths. SQL Server matches path keys against the escaped document text, so the columns escape keys exactly as the store's serializer writes them, and a test pins this. Unindexed queries find facets by enumerating keys with `OPENJSON` instead, which returns them unescaped.
- **Identifiers can't end in a space.** SQL Server's `=` pads trailing spaces even under BIN2, so `x` and `x ` would be one key. Identifier columns carry a check constraint that refuses trailing spaces, and the payload upsert compares lengths so it can never update another ID's row. Lookups by an ID with a trailing space still find the ID without it; the provider README says to trim identifiers.
- **Index key length limits:** tenant IDs up to 128 characters, other identifiers up to 256 and channels up to 64. The provider README documents them.
- **The insert trigger is part of the schema.** Hosts that apply schema in a deployment step (`InitializeSchema = false` with `IInfrastructureRunner`) get the trigger with the other infrastructure steps.
