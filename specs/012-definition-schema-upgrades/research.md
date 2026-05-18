# Research: Definition Schema Versions & Upgrade Flow

## Decision: Use Existing Resource Definition Version Lineage

**Decision**: Use the existing `Resource.DefinitionVersion` value as the resource-version lineage field for this slice.

**Rationale**: The current resource model already records the definition version active at creation time. The feature is about making that lineage explicit and actionable, not introducing a new storage shape. Reusing the existing field keeps the slice small, source-compatible, and provider-agnostic.

**Alternatives considered**:

- Add a separate `ResourceDefinitionVersionReference` object to every resource. Rejected because it adds model churn before a demonstrated need beyond `(DefinitionId, DefinitionVersion)`.
- Add a separate `ResourceVersion` entity. Rejected because the current architecture intentionally treats `Resource` as the version snapshot.

## Decision: Add A Small Schema Version Service

**Decision**: Introduce a focused SDK service for schema status and explicit upgrades rather than putting all schema-evolution behavior directly on `IResourceManager`.

**Rationale**: Schema status and upgrades are a distinct workflow. A small service can compose `IResourceManager` and `IResourceDefinitionStore` without changing the existing lifecycle contract more than necessary. This keeps normal create/update/activate APIs stable while still giving hosts an explicit schema-evolution entry point.

**Alternatives considered**:

- Add methods directly to `IResourceManager`. Rejected for this slice because it broadens the central lifecycle interface and makes schema-evolution behavior harder to delete or replace later.
- Create a general migration framework. Rejected because the spec explicitly excludes migrations, automatic rewriting, and transformation pipelines.

## Decision: Per-Version Schema Status

**Decision**: Schema status is evaluated for one resource version at a time.

**Rationale**: Aster resources are immutable version snapshots, and different versions of the same resource can legitimately reference different definition versions. Per-version status is direct, testable, and avoids ambiguous whole-resource summaries.

**Alternatives considered**:

- Whole-resource schema status summary. Rejected because it can hide mixed-version lineage and is derivable later.
- Provide both per-version and whole-resource status. Rejected because it expands the first slice beyond the current requirements.

## Decision: Preserve Carried-Forward Data By Default

**Decision**: Explicit upgrades preserve all existing aspect data by default, even when the target definition no longer declares that aspect, and report it as carried-forward data.

**Rationale**: The system is append-only and should avoid data loss by default. Reporting carried-forward data makes the behavior observable while keeping transformation decisions with the caller.

**Alternatives considered**:

- Drop undeclared aspect data during upgrade. Rejected because it violates the no automatic rewrite/data-loss posture.
- Fail upgrades with undeclared data. Rejected because it makes harmless schema-version advancement harder and forces callers to build transformation handling immediately.

## Decision: Latest Resource Version As Upgrade Source

**Decision**: Upgrades use the latest resource version as their source and fail through existing optimistic concurrency behavior when the caller's base version is stale.

**Rationale**: This matches the existing update and activation model. It avoids creating a new latest version from an arbitrary historical snapshot, which would be a separate promotion/branching workflow.

**Alternatives considered**:

- Allow upgrades from any historical version. Rejected because it complicates concurrency and can surprise callers by reviving old data.
- Allow historical upgrades only with opt-in. Rejected as a future extension; the first slice should keep one clear rule.

## Decision: Target Version Rules

**Decision**: Upgrades default to the latest definition version, but callers may explicitly target any existing definition version newer than the source lineage and not newer than the latest known definition version.

**Rationale**: This supports staged rollout and compatibility testing without allowing downgrades or imaginary future versions.

**Alternatives considered**:

- Always upgrade to latest only. Rejected because staged schema adoption is a reasonable near-term host need.
- Allow downgrades. Rejected because downgrade semantics imply data transformation and conflict rules outside this slice.

## Decision: Unknown Lineage Remains Unknown Until Upgrade

**Decision**: Resource versions without recorded definition version lineage are classified as `unknown-resource-lineage` and are not interpreted as latest or version 1 by assumption.

**Rationale**: Silent assumptions would make old data appear safer than it is. Explicit upgrade creates a new version with known target lineage once the caller chooses a target.

**Alternatives considered**:

- Treat missing lineage as latest. Rejected because it hides uncertainty.
- Treat missing lineage as version 1. Rejected because not all old data necessarily came from version 1.
