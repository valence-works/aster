# Data Model — Persistence & Querying Essentials (Phase 2)

## Entity: DefinitionSnapshot
- Purpose: Immutable schema snapshot used when creating resource versions.
- Fields:
  - `DefinitionId` (string, required) — logical definition identity.
  - `Version` (int, required, >= 1) — definition snapshot version.
  - `VersionId` (string, required) — unique version-row identifier.
  - `PayloadJson` (string, required) — serialized definition document.
  - `CreatedUtc` (datetime, required).
- Keys/Constraints:
  - Primary key: (`DefinitionId`, `Version`).
  - Unique: `VersionId`.
- Relationships:
  - Referenced by `ResourceVersionRecord.DefinitionId + DefinitionVersion`.

## Entity: ResourceVersionRecord
- Purpose: Immutable persisted version snapshot for a logical resource.
- Fields:
  - `ResourceId` (string, required) — logical identity across versions.
  - `Version` (int, required, >= 1) — monotonic version ordinal.
  - `VersionId` (string, required) — unique snapshot identifier.
  - `DefinitionId` (string, required).
  - `DefinitionVersion` (int, optional but strongly recommended).
  - `CreatedUtc` (datetime, required).
  - `Owner` (string, optional).
  - `AspectsJson` (string, required) — serialized aspect map.
  - `Hash` (string, optional).
- Keys/Constraints:
  - Primary key: (`ResourceId`, `Version`).
  - Unique: `VersionId`.
  - Foreign key: (`DefinitionId`, `DefinitionVersion`) -> `DefinitionSnapshot` when `DefinitionVersion` supplied.
- Relationships:
  - 1 logical `ResourceId` to many version rows.
  - Activation resolved via `ActivationRecord` by `ResourceId` + channel.

## Entity: ActivationRecord
- Purpose: Tracks active versions in a channel and channel policy.
- Fields:
  - `ResourceId` (string, required).
  - `Channel` (string, required, case-preserving, case-insensitive compare).
  - `Mode` (enum, required): `SingleActive` | `MultiActive`.
  - `ActiveVersionsJson` (string, required) — list of active version ordinals.
  - `LastUpdatedUtc` (datetime, required).
- Keys/Constraints:
  - Primary key: (`ResourceId`, `Channel`).
  - Referential constraint: every active version must exist in `ResourceVersionRecord` for same `ResourceId`.
  - Validation:
    - `SingleActive` -> exactly 0 or 1 active version.
    - `MultiActive` -> 0..N active versions, distinct values only.

## Entity: InfrastructureStepRecord
- Purpose: Idempotent ledger of provider infrastructure initialization/upgrade steps.
- Fields:
  - `StepId` (string, required) — globally unique step identity.
  - `AppliedUtc` (datetime, required).
  - `Checksum` (string, optional) — optional integrity marker.
  - `Status` (enum, required): `Applied` | `Failed`.
  - `Notes` (string, optional).
- Keys/Constraints:
  - Primary key: `StepId`.
  - Idempotency: applied step cannot be re-applied with changed checksum without explicit operator action.

## Entity: QueryRequest
- Purpose: Persisted-query execution shape used by provider adapter.
- Fields:
  - `DefinitionId` (string, optional).
  - `FilterTree` (object, optional) — AST from `ResourceQuery.Filter`.
  - `Sort` (list, optional) — one or more field order descriptors.
  - `Skip` (int, optional, >= 0).
  - `Take` (int, optional, > 0).
- Validation:
  - Operators limited to Phase 2 supported set.
  - Sort must always include deterministic tie-break (`ResourceId`, `Version`) when needed.
  - Missing sort values are included and ordered last.

## State Transitions

### Resource Lifecycle
1. `Create` -> append `Version=1` row in `ResourceVersionRecord`.
2. `Update` -> optimistic check against latest version; append `Version=N+1`.
3. `Activate(channel)` -> upsert `ActivationRecord` and apply mode rules.
4. `Deactivate(channel)` -> remove version from `ActiveVersions`; retain history.

### Infrastructure Lifecycle
1. `Uninitialized` -> apply ordered infra steps and write ledger entries.
2. `PartiallyApplied` -> retry pending/failed step safely.
3. `Current` -> no-op on subsequent apply when all required steps are recorded.

## Integrity Rules (Cross-Entity)
- `ResourceVersionRecord` is append-only; historical rows are immutable.
- `ActivationRecord.ActiveVersions` cannot reference non-existent versions.
- Deleting historical rows is out of Phase 2 scope and disallowed by default.
- Query results must map to latest version per resource unless query explicitly requests historical versions.
