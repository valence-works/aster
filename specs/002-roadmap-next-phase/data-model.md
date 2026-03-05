# Data Model — Persistence & Querying Essentials (Phase 2)

> **Mapping convention**: Each persistence record is a 1-to-1 serialised form of its corresponding `Aster.Core` domain model. Field names are aligned with the domain model properties. No domain types are combined or split across records.

---

## Entity: ResourceDefinitionRecord

Persists an immutable version of `Aster.Core.Models.Definitions.ResourceDefinition` (including its embedded `AspectDefinition` and `FacetDefinition` snapshots). Maps directly to what `IResourceDefinitionStore.RegisterDefinitionAsync` appends.

- Fields:
  - `DefinitionId` (string, required) — logical definition identity, shared across all versions. Maps to `ResourceDefinition.DefinitionId`.
  - `Version` (int, required, >= 1) — monotonic version ordinal, auto-incremented by the store. Maps to `ResourceDefinition.Version`.
  - `VersionId` (string, required) — unique snapshot identifier (GUID). Maps to `ResourceDefinition.Id`.
  - `IsSingleton` (bool, required) — denormalised from payload to allow singleton enforcement without deserialising. Maps to `ResourceDefinition.IsSingleton`.
  - `PayloadJson` (string, required) — full serialised `ResourceDefinition` document, including all embedded `AspectDefinition` and `FacetDefinition` snapshots. Drives runtime hydration.
  - `CreatedUtc` (datetime, required) — recorded at write time.
- Keys/Constraints:
  - Primary key: (`DefinitionId`, `Version`).
  - Unique: `VersionId`.
- Notes:
  - `AspectDefinition` and `FacetDefinition` are embedded within `PayloadJson`; they are not stored in separate tables (mirrors Phase 1 embedding rule).
  - Rows are immutable after insert; no update path exists.

---

## Entity: ResourceRecord

Persists an immutable version snapshot of `Aster.Core.Models.Instances.Resource`. Maps to what `IResourceWriteStore.SaveVersionAsync` appends.

- Fields:
  - `ResourceId` (string, required) — logical resource identity, shared across all versions. Maps to `Resource.ResourceId`.
  - `Version` (int, required, >= 1) — monotonic version ordinal. Maps to `Resource.Version`.
  - `VersionId` (string, required) — unique snapshot identifier (GUID). Maps to `Resource.Id`.
  - `DefinitionId` (string, required) — logical definition the resource conforms to. Maps to `Resource.DefinitionId`.
  - `DefinitionVersion` (int, optional) — definition version active at creation time, for traceability. Maps to `Resource.DefinitionVersion`. Advisory only: the system returns the resource as-is regardless of whether the referenced definition version is loaded in the runtime store.
  - `AspectsJson` (string, required) — serialised `Resource.Aspects` map (keys are `AspectDefinitionId` or `"{AspectDefinitionId}:{Name}"` composites).
  - `CreatedUtc` (datetime, required) — maps to `Resource.Created`.
  - `Owner` (string, optional) — maps to `Resource.Owner`.
  - `Hash` (string, optional) — maps to `Resource.Hash`.
- Keys/Constraints:
  - Primary key: (`ResourceId`, `Version`).
  - Unique: `VersionId`.
  - Soft referential: `DefinitionId` is expected to exist in `ResourceDefinitionRecord`; no hard FK enforced (definition may evolve independently).
- Notes:
  - Rows are immutable after insert; no update path exists.
  - Status (draft vs active) is derived from `ActivationRecord`, not stored here.

---

## Entity: ActivationRecord

Persists the mutable activation state for a `(ResourceId, Channel)` pair. Maps to `Aster.Core.Models.Instances.ActivationState` and is written by `IResourceWriteStore.UpdateActivationAsync`.

- Fields:
  - `ResourceId` (string, required) — maps to `ActivationState.ResourceId`.
  - `Channel` (string, required, case-preserving, case-insensitive compare) — maps to `ActivationState.Channel`.
  - `Mode` (enum, required): `SingleActive` | `MultiActive` — durable per-channel policy. Set on first activation of a channel; may be updated by a subsequent caller-supplied mode. Maps to `ActivationState.Mode` (Phase 2 addition to the domain model — see Domain Model Extensions below).
  - `ActiveVersionsJson` (string, required) — serialised list of active `Resource.Version` ordinals. Maps to `ActivationState.ActiveVersions`.
  - `LastUpdatedUtc` (datetime, required) — maps to `ActivationState.LastUpdated`.
- Keys/Constraints:
  - Primary key: (`ResourceId`, `Channel`).
  - Referential constraint: every version ordinal in `ActiveVersionsJson` must exist as a `ResourceRecord` row for the same `ResourceId`.
  - Validation (enforced by the manager, not the store):
    - `SingleActive` → at most 1 version ordinal in `ActiveVersionsJson` after any activation call.
    - `MultiActive` → 0..N distinct version ordinals.
- Notes:
  - An explicit `ChannelMode` MUST be supplied on the first `ActivateAsync` call for a channel; omitting it MUST return a typed `ValidationFailed` error.
  - An explicit mode supplied on a subsequent call updates the stored mode.
  - Once a mode is stored, calling `ActivateAsync` without a mode uses the stored mode.
  - Row is upserted on each activation or deactivation change; it is not append-only.

---

## State Transitions

### Definition Lifecycle
1. `RegisterDefinition` → insert new `ResourceDefinitionRecord` row with `Version = max(existing) + 1`. Existing rows untouched.

### Resource Lifecycle
1. `Create` → insert `ResourceRecord` with `Version = 1`. No prior row for this `ResourceId`.
2. `Update` → optimistic check that `Version = max(existing)`; insert `ResourceRecord` with `Version = N + 1`.
3. `Activate(channel, mode?)` → if no `ActivationRecord` exists for the channel and `mode` is null, return a typed `ValidationFailed` error; otherwise upsert `ActivationRecord`; store or update `Mode` if supplied, otherwise use stored mode; add version ordinal to `ActiveVersionsJson` subject to mode enforcement.
4. `Deactivate(channel)` → update `ActivationRecord`; remove version ordinal from `ActiveVersionsJson`. History in `ResourceRecord` is unchanged.

---

## Integrity Rules

- `ResourceDefinitionRecord` rows are append-only and immutable after insert.
- `ResourceRecord` rows are append-only and immutable after insert.
- `ActivationRecord.ActiveVersionsJson` may only reference version ordinals present in `ResourceRecord` for the same `ResourceId`.
- Deletion of any historical row is out of Phase 2 scope and disallowed by default.

---

## Domain Model Extensions (Phase 2)

The following `Aster.Core` types require extension to support durable per-channel policy. These are additive changes; Phase 1 behaviour is preserved.

### `ActivationState` — add `Mode`

| Field | Type | Description |
|---|---|---|
| `Mode` | `ChannelMode` enum | `SingleActive` \| `MultiActive`. Stored per channel. An explicit mode MUST be supplied on first activation; omitting it MUST return a typed `ValidationFailed` error. Subsequent calls may omit mode to reuse the stored value. |

### `IResourceManager.ActivateAsync` — replace `allowMultipleActive` with `ChannelMode?`

Current signature (Phase 1):
```
ActivateAsync(string resourceId, int version, string channel, bool allowMultipleActive = false, ...)
```
Phase 2 signature:
```
ActivateAsync(string resourceId, int version, string channel, ChannelMode? mode = null, ...)
```
- `mode = null` → use the stored mode for the channel. If no `ActivationRecord` exists yet for this channel, a typed `ValidationFailed` error is returned — a mode must be supplied explicitly on first activation.
- `mode = ChannelMode.SingleActive` or `MultiActive` → set or update stored mode, then enforce.

---

## Query Shape

`ResourceQuery` (from `Aster.Core.Models.Querying`) is not a stored entity. The provider translates it to parameterised SQL at execution time. Query contract details — supported operators, sort semantics, and paging rules — are specified in `contracts/persistence-query-contract.md`.
