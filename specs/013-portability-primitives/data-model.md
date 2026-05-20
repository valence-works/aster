# Data Model: Portability Primitives

## Portable Snapshot

A self-contained SDK snapshot for moving definitions, resources, resource versions, and activation state.

**Fields**:

- `FormatVersion`: snapshot format version, initially `1`.
- `Definitions`: definition version snapshots.
- `Resources`: resource version snapshots.
- `ActivationStates`: activation entries for exported resource versions.
- `SkippedActivations`: activation entries omitted because their resource versions were not exported.

**Validation rules**:

- Format version must be supported.
- Definition entries must have unique `(DefinitionId, Version)` pairs.
- Resource entries must have unique `(ResourceId, Version)` pairs and unique version-specific `Id` values.
- Every resource with known definition lineage must reference a definition version present in the snapshot.
- Activation entries must reference resource versions present in the snapshot.
- Skipped activation entries are diagnostics and are not imported.

## Export Request

Caller-selected scope for creating a portable snapshot.

**Fields**:

- `ExportScopeMode`: `DefinitionsOnly`, `SelectedResources`, or `DefinitionWithResources`.
- `DefinitionIds`: selected definition identifiers.
- `ResourceIds`: selected resource identifiers.
- `ResourceVersionScope`: `AllVersions`, `LatestOnly`, or `SpecificVersions`.
- `SpecificResourceVersions`: optional selected `(ResourceId, Version)` pairs.

**Validation rules**:

- Definitions-only exports require at least one definition identifier.
- Selected-resources exports require at least one resource identifier or specific resource version.
- Definition-with-resources exports require at least one definition identifier.
- Specific-version scope requires at least one specific resource version.
- Export fails with diagnostics when requested definitions or resource versions cannot be found.

## Import Options

Caller-selected import behavior.

**Fields**:

- `CollisionMode`: `Strict` by default, or `RemapDivergent`.

**Validation rules**:

- Strict mode fails before writing when divergent collisions exist.
- Remap mode plans deterministic replacement identifiers for divergent collisions.
- Identical existing content is treated as already satisfied in all modes.

## Import Preview

Non-mutating result describing what a write import would do.

**Fields**:

- `Counts`: planned counts for definitions, resources, resource versions, and activation entries.
- `IdentityMap`: original-to-imported identity mappings.
- `Diagnostics`: validation, collision, and remap diagnostics.
- `CanImport`: true only when no error diagnostics remain.

**Validation rules**:

- Preview must not mutate the target store.
- Repeating preview with the same snapshot and target state must produce the same identity map and diagnostics.

## Import Result

Write import outcome.

**Fields**:

- `Counts`: imported or reused counts.
- `IdentityMap`: original-to-imported identity mappings used for writes.
- `Diagnostics`: warnings and informational diagnostics.
- `Status`: `Imported`, `NoOp`, or `Failed`.

**Validation rules**:

- Failed imports leave no partial definitions, resources, resource versions, or activation entries.
- No-op import is valid when all snapshot content already exists with identical content.
- Successful import preserves snapshot relationships after remapping.

## Identity Mapping

Original-to-imported identifier relationship.

**Fields**:

- `EntityKind`: definition, definition version, resource, resource version, or activation entry.
- `OriginalId`: identifier from the snapshot.
- `ImportedId`: identifier used in the target store.
- `Reason`: `Preserved`, `ReusedIdentical`, or `RemappedCollision`.

**Validation rules**:

- Remapped definition identifiers update resource `DefinitionId` and lineage references.
- Remapped resource identifiers update resource versions and activation entries.
- Mapping output is deterministic for the same snapshot and target-store state.

## Portability Diagnostic

Structured diagnostic emitted by export, validation, preview, and import.

**Fields**:

- `Code`: stable machine-readable code.
- `Severity`: `Info`, `Warning`, or `Error`.
- `Path`: optional snapshot/request path.
- `Message`: human-readable explanation.

**Common codes**:

- `missing-definition`
- `missing-definition-version`
- `missing-resource`
- `missing-resource-version`
- `duplicate-snapshot-identity`
- `unresolved-definition-reference`
- `unresolved-resource-reference`
- `divergent-identity-collision`
- `unsupported-snapshot-format`
- `malformed-snapshot`
- `skipped-activation-entry`

## State Transitions

```text
Export request
  -> Resolve explicit scope
  -> Read exact provider snapshots
  -> Validate snapshot references
  -> Return snapshot + skipped activation diagnostics

Snapshot import preview
  -> Validate snapshot shape
  -> Compare target identities
  -> Reuse identical content
  -> Fail divergent collisions in strict mode
  -> Plan deterministic mappings in remap mode
  -> Return non-mutating preview

Snapshot write import
  -> Run same validation/planning as preview
  -> Provider applies planned snapshot atomically
  -> Return import result
```
