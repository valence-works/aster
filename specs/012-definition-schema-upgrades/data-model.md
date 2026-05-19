# Data Model: Definition Schema Versions & Upgrade Flow

## Resource Definition Version

An immutable schema snapshot for a resource definition.

**Existing fields used**:

- `DefinitionId`: stable logical identifier.
- `Version`: immutable definition version number.
- `AspectDefinitions`: declared aspect attachments for that definition version.
- `IsSingleton`: singleton policy for the definition version.

**Validation rules**:

- The latest version is resolved through the definition store.
- A specific target version must exist before an upgrade can use it.

## Resource Version Lineage

The link from a resource version snapshot to the definition version that governed it.

**Existing fields used**:

- `Resource.DefinitionId`
- `Resource.DefinitionVersion`
- `Resource.Version`

**Validation rules**:

- New resource creation records the latest available definition version.
- Normal resource update preserves the source resource version's `DefinitionVersion`.
- Missing `DefinitionVersion` is `unknown-resource-lineage`.
- Historical resource versions are never rewritten when definitions change.

## Schema Status

A diagnostic result for one resource version.

**Fields**:

- `ResourceId`
- `ResourceVersion`
- `DefinitionId`
- `RecordedDefinitionVersion`
- `LatestDefinitionVersion`
- `Status`
- `Message`

**Status values**:

- `Current`: recorded definition version equals latest definition version.
- `OlderThanLatest`: recorded definition version exists and is lower than latest.
- `MissingDefinition`: no definition exists for the resource's definition identifier.
- `MissingDefinitionVersion`: definition exists, but the recorded version does not.
- `UnknownResourceLineage`: resource version does not record definition version lineage.

**Validation rules**:

- Status is always for one requested resource version.
- A recorded definition version greater than latest is treated as `MissingDefinitionVersion`.
- Status never upgrades or mutates a resource.

## Upgrade Request

A caller-initiated request to append a new resource version with target definition lineage.

**Fields**:

- `BaseVersion`: optimistic concurrency token, matching current latest resource version.
- `TargetDefinitionVersion`: optional explicit target; defaults to latest definition version.
- `AspectUpdates`: optional explicit aspect changes applied during upgrade.

**Validation rules**:

- Base version must match latest resource version.
- Target definition version must exist.
- For known source lineage, target must be greater than source lineage and not greater than latest.
- Target equal to source lineage returns no-op rather than appending a duplicate version.
- Unknown source lineage may upgrade to an existing target version when concurrency checks pass.

## Upgrade Result

The outcome of an explicit upgrade request.

**Fields**:

- `Status`
- `Resource`
- `SourceDefinitionVersion`
- `TargetDefinitionVersion`
- `CarriedForwardAspectKeys`
- `Message`

**Status values**:

- `Upgraded`: a new resource version was created.
- `NoOp`: no new version was created because source and target definition versions already match.

**Failure behavior**:

- Missing target definition version fails with structured diagnostics.
- Stale base version fails with existing optimistic concurrency behavior.
- Invalid target ordering fails with structured diagnostics.

## Carried-Forward Data

Aspect data preserved during upgrade even though the target definition version does not declare that aspect key.

**Fields**:

- Aspect key.

**Validation rules**:

- Carried-forward data is reported, not removed.
- Callers may explicitly change aspect data during upgrade through aspect updates.

## State Transitions

```text
Current resource version
  ├─ Check schema status -> diagnostic only, no mutation
  ├─ Normal update -> new resource version, same DefinitionVersion
  └─ Explicit upgrade
      ├─ target equals source -> NoOp
      └─ target newer/existing -> new resource version with TargetDefinitionVersion
```
