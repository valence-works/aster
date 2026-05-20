# Research: Portability Primitives

## Decision: Add A Public Portability Service

**Decision**: Introduce `IResourcePortabilityService` as the public SDK workflow for export, snapshot validation, import preview, and write import.

**Rationale**: Portability is a distinct SDK workflow that coordinates definitions, resources, activation state, validation, identity mapping, and diagnostics. Keeping it in a focused service avoids expanding `IResourceManager` and keeps import/export behavior discoverable.

**Alternatives considered**:

- Add export/import methods to `IResourceManager`. Rejected because the manager is resource lifecycle oriented and does not own definitions or snapshot validation.
- Add static helper methods. Rejected because the workflow needs provider-backed collaborators and cancellation-aware async operations.
- Build a recipe or hook framework first. Rejected because the spec explicitly defers recipes and lifecycle hooks.

## Decision: Add A Narrow Provider-Facing Portability Store

**Decision**: Add an explicit provider-facing `IResourcePortabilityStore` for snapshot enumeration, collision lookup, and all-or-nothing snapshot writes.

**Rationale**: Existing lifecycle contracts intentionally hide storage details. They do not enumerate all definition versions, do not expose all activation channels, and `RegisterDefinitionAsync` auto-assigns definition versions rather than preserving arbitrary snapshot version numbers. A narrow portability store is justified by this concrete gap and keeps provider-specific storage access out of the public service implementation.

**Alternatives considered**:

- Use only `IResourceDefinitionStore`, `IResourceVersionReader`, and `IResourceVersionWriter`. Rejected because exact import of definition versions and complete activation export are not possible through those contracts.
- Query provider storage via `IResourceQueryService`. Rejected because export/import is not a query concern and must not depend on provider query capabilities.
- Add many small reader/writer interfaces. Rejected because one cohesive portability store is simpler for providers to implement and easier to delete later.

## Decision: SDK-Native Snapshot Model

**Decision**: Represent snapshots as SDK-native records containing format metadata, `ResourceDefinition` snapshots, `Resource` version snapshots, `ActivationState` entries, diagnostics, skipped activation entries, and identity mapping entries.

**Rationale**: The first format is for SDK-to-SDK portability, not a long-term archival package standard. SDK-native records can use existing serialization behavior, avoid new dependencies, and remain easy to test.

**Alternatives considered**:

- Define a compressed package format. Rejected because compression, manifests, signing, and file transport are outside this slice.
- Use provider-specific dump formats. Rejected because portability must be provider-agnostic.
- Use external schema/document packages. Rejected because dependencies should remain intentional and no current requirement needs them.

## Decision: Explicit Export Scope And Version Scope

**Decision**: Export requests require explicit export scope: `definitions-only`, `selected-resources`, or `definition-with-resources`; resource exports also require explicit resource version scope: all versions, latest only, or specific versions.

**Rationale**: Exporting an unintended whole store is a practical risk. Explicit modes make the package contents predictable and directly testable.

**Alternatives considered**:

- Always export all resources for selected definitions. Rejected because it can move more data than intended.
- Always export only selected resources. Rejected because definition-with-resources is a valid operational workflow.
- Always export all resource versions. Rejected because small packages and latest-only promotion are common needs.

## Decision: Activation Entries Follow Exported Versions

**Decision**: Include activation entries only for exported resource versions. If active versions are omitted by scope, report skipped activation entries.

**Rationale**: This keeps partial-version exports valid and avoids dangling activation references. Reporting skipped entries makes the loss of activation context observable.

**Alternatives considered**:

- Fail export when active versions are omitted. Rejected because latest-only and specific-version exports should remain usable.
- Automatically include active versions even when not selected. Rejected because it weakens explicit version scope.
- Drop activation state silently. Rejected because activation is observable resource behavior.

## Decision: Strict Import By Default

**Decision**: Import uses strict collision handling by default. Deterministic remapping must be explicitly requested.

**Rationale**: Remapping changes identities. Strict-by-default behavior is safer and aligns with explicitness over magic.

**Alternatives considered**:

- Remap automatically. Rejected because identity changes could surprise callers and hosts.
- Require callers to choose strict/remap every time. Rejected because strict is a safe default and reduces boilerplate.

## Decision: Identical Existing Content Is Already Satisfied

**Decision**: If the target store already contains the same identifier and identical content, import treats that item as already satisfied. Divergent content is a collision.

**Rationale**: This makes repeated imports idempotent for unchanged content while still failing closed when content differs.

**Alternatives considered**:

- Treat any existing identifier as a collision. Rejected because it makes idempotent import unnecessarily noisy.
- Always remap existing identifiers in remap mode. Rejected because identical content should not create duplicate logical data.

## Decision: Deterministic Remapping

**Decision**: Deterministic replacement identifiers are derived from original identifiers, entity kind, and target-store collision state. They must not use wall-clock time or random IDs.

**Rationale**: Preview and write import need to produce the same identity map for the same snapshot and target state. Determinism is also required for repeatable tests.

**Alternatives considered**:

- Use `IIdentityGenerator` for remapped IDs. Rejected because the default generator is random GUID-based and would make previews non-repeatable.
- Let providers choose remapped IDs. Rejected because identity mapping must be consistent across providers.

## Decision: All-Or-Nothing Write Import

**Decision**: Write import validates and plans before writing, then asks the portability store to apply the planned snapshot atomically.

**Rationale**: The spec requires failed imports to leave no partial data. Providers own their transaction or staging strategy: SQLite can use a transaction; in-memory can stage copies before replacing state.

**Alternatives considered**:

- Write incrementally through existing lifecycle APIs. Rejected because partial writes would be possible and definition versions could be renumbered.
- Add rollback logic in the service. Rejected because rollback is provider-specific and less reliable than provider-owned atomic apply.
