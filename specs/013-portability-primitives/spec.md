# Feature Specification: Portability Primitives

**Feature Branch**: `013-portability-primitives`
**Created**: 2026-05-19
**Status**: Draft
**Input**: User description: "Start Phase 4 with deterministic export/import primitives for definitions and resources. Export definition and resource snapshots with stable references. Import with deterministic ID remapping and collision diagnostics. Keep recipes and host lifecycle hooks separate follow-up slices."

## Clarifications

### Session 2026-05-19

- Q: How should import treat an incoming identifier that already exists in the target store with identical content? → A: Identical existing content is reused/no-op; divergent content is a collision.
- Q: When exporting by definition, should the snapshot include all resources for that definition or only explicitly selected resources? → A: Support both modes; caller must choose resource scope explicitly.
- Q: Which versions of selected resources should export include? → A: Caller chooses all versions, latest only, or specific versions.
- Q: How should activation state be handled when selected resource versions omit active versions? → A: Include only activation entries for exported versions and report skipped entries.
- Q: What should the default import behavior be when divergent identifier collisions are detected? → A: Strict by default; caller must opt into deterministic remapping.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Export A Portable Snapshot (Priority: P1)

As an SDK user, I want to export selected resource definitions and resources into a portable snapshot so I can move content between stores without depending on provider-specific storage.

**Why this priority**: Export is the foundation for portability. Without a self-contained snapshot, import behavior cannot be tested or trusted.

**Independent Test**: Create definition versions, resources, resource versions, and activations; export a selected scope; verify the snapshot contains the requested data plus all references required to understand it.

**Acceptance Scenarios**:

1. **Given** definitions and resources exist in a store, **When** the caller exports selected definitions without selecting resources, **Then** the snapshot contains those definition versions with stable identifiers and version numbers.
2. **Given** resources are selected for export, **When** the snapshot is created, **Then** it contains resource versions according to the caller's all-versions, latest-only, or specific-versions selection and the definition versions those resource versions reference.
3. **Given** a caller chooses definition-with-resources scope, **When** the snapshot is created, **Then** it includes resources for the selected definitions and the definition versions those resources reference.
4. **Given** exported resource versions have activation channels, **When** the snapshot is created, **Then** activation state for exported versions is included.
5. **Given** active resource versions are omitted by the selected resource version scope, **When** the snapshot is created, **Then** activation entries for omitted versions are skipped and reported.
6. **Given** an export scope references missing data, **When** export is requested, **Then** the caller receives structured diagnostics and no partial snapshot is reported as complete.

---

### User Story 2 - Import With Deterministic Identity Mapping (Priority: P2)

As an SDK user, I want to import a portable snapshot into another store with predictable identity mapping so imported definitions and resources remain internally consistent even when target identifiers collide.

**Why this priority**: Portability is only useful if imports are repeatable, explainable, and safe when the target store already contains data.

**Independent Test**: Export a snapshot, import it into an empty store and into a store with colliding identifiers, and verify the imported data preserves relationships through a deterministic original-to-imported identity map.

**Acceptance Scenarios**:

1. **Given** a valid snapshot and an empty target store, **When** the snapshot is imported, **Then** definitions, resources, versions, and activation state are recreated with their original identifiers when no collision exists.
2. **Given** the target store already contains one or more divergent identifiers from the snapshot, **When** import runs in explicit remap mode, **Then** colliding incoming identifiers are mapped to deterministic replacement identifiers and all internal references use the replacement identifiers consistently.
3. **Given** the same snapshot and same target state, **When** remap import is repeated as a preview, **Then** the same identity map and diagnostics are produced.
4. **Given** the target store contains a matching identifier with identical content, **When** import is requested, **Then** the import treats that item as already satisfied and does not create duplicate data.
5. **Given** the target store contains a conflicting identifier and the caller has not explicitly allowed remapping, **When** import is requested, **Then** the import fails before writing data and reports the collision.

---

### User Story 3 - Preview Import Diagnostics Before Writing (Priority: P3)

As a host application, I want to preview import results and diagnostics before writing to the target store so users can decide whether to proceed, remap, or fix the package.

**Why this priority**: Import can affect many definitions and resources. A preview gives hosts a safe integration point without requiring lifecycle hooks in this slice.

**Independent Test**: Run import preview against valid, colliding, and invalid snapshots; verify diagnostics, counts, and identity mappings are returned without changing the target store.

**Acceptance Scenarios**:

1. **Given** a valid snapshot, **When** import preview is requested, **Then** the caller receives counts for definitions, resources, resource versions, activation entries, and planned identity mappings.
2. **Given** a snapshot contains unresolved references, **When** import preview is requested, **Then** diagnostics identify the missing or invalid references.
3. **Given** import preview completes, **When** the target store is inspected, **Then** no definitions, resources, versions, or activations have been written.

---

### Edge Cases

- The export scope includes a resource whose recorded definition version is missing.
- The export scope includes a resource with unknown definition lineage.
- The caller exports only definitions, with no resources.
- The caller exports specific resources for a definition that has many other resources.
- The caller exports a definition-with-resources scope for one or more selected definitions.
- The caller exports only latest resource versions while older versions exist.
- The caller exports a specific subset of resource versions for a resource with many versions.
- The caller exports a specific resource version subset that omits active versions in one or more activation channels.
- Multiple resource versions in the same export reference different definition versions.
- The target store already contains a definition identifier from the snapshot with different version content.
- The target store already contains a resource identifier from the snapshot.
- The target store already contains an identifier from the snapshot with identical content.
- The target store contains divergent identifier collisions and the caller did not explicitly choose remap mode.
- The snapshot contains duplicate identifiers or duplicate version numbers.
- The snapshot references activation state for a resource version that is not present in the snapshot.
- A remapped definition identifier must also update all resource lineage references in the imported data.
- A remapped resource identifier must also update all resource versions and activation entries in the imported data.
- An import is attempted from an unsupported or malformed snapshot shape.
- An import preview succeeds, but the target store changes before the write import is attempted.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The slice defines portable snapshots, validation, deterministic identity mapping, preview, and write import only. It excludes recipes, host lifecycle hooks, live sync, external storage adapters, package signing, data transforms, and migration engines.
- **Explicitness**: Callers choose export scope, resource inclusion mode, import mode, and whether an import is preview-only or writes data. Import is strict by default, and remapping must be explicitly selected. Collisions and remapping are reported explicitly.
- **Dependencies**: None.
- **Operational Impact**: Existing provider setup remains unchanged. Portability uses existing definition and resource stores and adds no background processes or deployment infrastructure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a way to export selected resource definitions and resources into a portable snapshot.
- **FR-002**: Exported snapshots MUST include stable identifiers and version numbers for exported definitions, resources, resource versions, and activation state.
- **FR-003**: Export scope MUST let callers explicitly choose definitions-only, selected-resources, or definition-with-resources export modes.
- **FR-004**: Resource export scope MUST let callers explicitly choose all versions, latest only, or specific versions for included resources.
- **FR-005**: Exporting resources MUST include the definition versions referenced by those exported resource versions, even when those definitions were not explicitly selected.
- **FR-006**: Export MUST preserve resource definition lineage for each resource version.
- **FR-007**: Export MUST preserve all exported resource versions without rewriting historical versions.
- **FR-008**: Export MUST preserve activation channel membership for exported resource versions.
- **FR-009**: Export MUST skip activation entries that reference omitted resource versions and MUST report those skipped activation entries.
- **FR-010**: Export MUST fail with structured diagnostics when required referenced data is missing.
- **FR-011**: The system MUST provide a way to validate a snapshot before writing it to a target store.
- **FR-012**: Snapshot validation MUST detect duplicate identifiers, duplicate version numbers within the same logical entity, unresolved definition references, unresolved resource references, and malformed snapshot content.
- **FR-013**: The system MUST provide an import preview that returns planned counts, identity mappings, and diagnostics without writing to the target store.
- **FR-014**: The system MUST provide a write import that recreates valid snapshot content in the target store.
- **FR-015**: Import MUST preserve relationships between definitions, resources, resource versions, lineage references, and activation state.
- **FR-016**: Import MUST preserve original identifiers when no target-store collision exists.
- **FR-017**: Import MUST treat an existing target-store item with the same identifier and identical content as already satisfied, without creating duplicate data.
- **FR-018**: Import MUST detect collisions between snapshot identifiers and target-store identifiers when the existing target-store content differs from the snapshot content.
- **FR-019**: Import MUST fail before writing data when divergent collisions exist and remapping is not explicitly allowed.
- **FR-020**: Import MUST use strict collision handling by default.
- **FR-021**: Import SHOULD support an explicit remap mode that maps colliding incoming identifiers to deterministic replacement identifiers.
- **FR-022**: Deterministic remapping MUST produce the same original-to-imported identity map for the same snapshot and target-store state.
- **FR-023**: Deterministic remapping MUST update all internal references in imported data consistently.
- **FR-024**: Import results MUST report the original identifier, imported identifier, entity kind, and collision reason for every remapped or failed identity.
- **FR-025**: Import MUST be all-or-nothing for a single requested snapshot; failed imports MUST NOT leave partial imported definitions, resources, versions, or activation entries.
- **FR-026**: Import preview and write import MUST report diagnostics using stable machine-readable codes and human-readable messages.
- **FR-027**: The snapshot format MUST include a format version so future portability changes can be detected safely.
- **FR-028**: The feature MUST NOT introduce recipes, host lifecycle hooks, live synchronization, runtime scanning, provider registries, migration engines, public SQL, or public `IQueryable<Resource>`.

### Key Entities *(include if feature involves data)*

- **Portable Snapshot**: A self-contained export package containing format metadata, definitions, resources, resource versions, activation state, and stable references between them.
- **Export Scope**: The caller-selected definitions-only, selected-resources, or definition-with-resources mode that determines which definitions and resources are included in a snapshot.
- **Resource Version Scope**: The caller-selected all-versions, latest-only, or specific-versions mode that determines which versions of included resources are exported.
- **Import Request**: A caller-initiated operation that validates or writes a snapshot into a target store.
- **Import Preview**: A non-mutating result that describes planned imported content, identity mappings, and diagnostics.
- **Identity Mapping**: The original-to-imported identifier relationship for definitions, resources, resource versions, and activation references.
- **Collision Diagnostic**: A structured report explaining why a snapshot identity conflicts with target-store data or cannot be imported.

### Assumptions

- Portability operates on existing definition and resource stores rather than introducing a separate package repository.
- The first snapshot format is intended for SDK-to-SDK portability, not long-term archival guarantees.
- Export/import includes activation channel state because activation is part of observable resource behavior.
- Deterministic remapping is based on snapshot content and target-store state, not on wall-clock time or random identity generation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A caller can export a store containing multiple definition versions, multiple resource versions, and activation state, then inspect a self-contained snapshot that preserves all required references and reports skipped activation entries.
- **SC-002**: A caller can import a valid snapshot into an empty target store and observe equivalent definitions, resources, versions, lineage, and activation state.
- **SC-003**: A caller can preview import into a target store with identical existing content and divergent identifier collisions, receiving no-op reuse decisions, deterministic identity mappings, and collision diagnostics without changing the target store.
- **SC-004**: Repeating import preview with the same snapshot and target-store state produces the same identity mapping and diagnostics.
- **SC-005**: Importing an invalid or conflicting snapshot without remapping leaves the target store unchanged and reports stable diagnostic codes.
- **SC-006**: Existing resource lifecycle, schema-version, query, and provider tests continue to pass without new infrastructure.
