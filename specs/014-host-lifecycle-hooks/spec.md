# Feature Specification: Host Lifecycle Hooks

**Feature Branch**: `014-host-lifecycle-hooks`  
**Created**: 2026-05-23  
**Status**: Draft  
**Input**: User description: "Add explicit host lifecycle hooks around resource save, activation, deactivation, export, preview import, and write import. Hooks should be registered explicitly by hosts, run in deterministic order, support cancellation/failure semantics, and expose structured diagnostics where applicable. Keep recipes, runtime scanning, live sync, background job orchestration, provider registries, public SQL, and IQueryable out of scope."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run Save Hooks Around Resource Mutations (Priority: P1)

As a host application, I want explicitly registered hooks to run before and after resource save operations so I can enforce host-specific policies and observe successful changes without modifying the resource manager itself.

**Why this priority**: Save operations are the most common mutation point. Policy checks, validation, auditing, and enrichment need a stable integration point before lower-priority activation and portability hooks add broader coverage.

**Independent Test**: Register two hooks, save a resource through create, update, and schema-upgrade flows, and verify hook order, operation context, cancellation, failure behavior, and after-success observation without changing unrelated query or storage behavior.

**Acceptance Scenarios**:

1. **Given** multiple save hooks are registered, **When** a resource is created or updated, **Then** before hooks run in deterministic registration order before the write and after hooks run in deterministic registration order after a successful write.
2. **Given** a before-save hook rejects a save, **When** the host attempts to create, update, or schema-upgrade a resource, **Then** the write does not occur and the host receives a clear structured failure.
3. **Given** a save operation succeeds, **When** after-save hooks run, **Then** each hook can inspect the saved resource identity, version, definition lineage, and operation kind.
4. **Given** a save operation fails before persistence for another reason, **When** hooks are registered, **Then** after-success hooks do not run.

---

### User Story 2 - Run Activation Hooks Around Channel Changes (Priority: P2)

As a host application, I want hooks around activation and deactivation so publishing workflows, audit systems, and policy checks can react to channel membership changes.

**Why this priority**: Activation is externally visible behavior. Hosts need to enforce publication rules and observe changes separately from resource saves.

**Independent Test**: Register hooks for activation and deactivation, activate and deactivate resource versions in named channels, and verify context, ordering, cancellation, and failure behavior independently of save hooks.

**Acceptance Scenarios**:

1. **Given** activation hooks are registered, **When** a resource version is activated in a channel, **Then** before-activation hooks run before the channel change and after-activation hooks run after the successful channel change.
2. **Given** a before-activation hook rejects an activation, **When** activation is requested, **Then** channel state remains unchanged and the host receives a clear structured failure.
3. **Given** deactivation hooks are registered, **When** a resource version is removed from a channel, **Then** before-deactivation and after-deactivation hooks receive the resource ID, version, channel, and resulting operation outcome.
4. **Given** an activation request allows multiple active versions, **When** hooks run, **Then** the hook context makes that choice visible.

---

### User Story 3 - Run Portability Hooks Around Export And Import (Priority: P3)

As a host application, I want hooks around export, import preview, and write import so I can enforce package policies and observe portability operations without introducing recipes or live synchronization.

**Why this priority**: Portability operations can move many definitions and resources at once. Hosts need a controlled integration point, but this should build on the already-completed portability primitives and not expand into a workflow engine.

**Independent Test**: Register portability hooks, export and preview/import snapshots, and verify hook order, snapshot/import context, cancellation, diagnostics, and non-mutation behavior for preview operations.

**Acceptance Scenarios**:

1. **Given** export hooks are registered, **When** a snapshot export is requested, **Then** before-export hooks run before snapshot materialization and after-export hooks run after a successful export result.
2. **Given** a before-export hook rejects an export, **When** export is requested, **Then** no snapshot is reported as complete and the host receives a structured diagnostic.
3. **Given** preview-import hooks are registered, **When** import preview runs, **Then** hooks can inspect the snapshot and preview outcome while the target store remains unchanged.
4. **Given** write-import hooks are registered, **When** import succeeds, **Then** after-import hooks can inspect status, identity mappings, counts, and diagnostics.
5. **Given** import planning fails, **When** hooks are registered, **Then** after-success hooks do not claim that an import was applied.

---

### Edge Cases

- Multiple hooks are registered for the same lifecycle point.
- A hook is registered more than once.
- A before hook rejects the operation after earlier hooks have already run.
- A hook throws or otherwise fails unexpectedly.
- A cancellation request is raised while hooks are running.
- A before hook rejects an operation that would otherwise have failed validation.
- An after hook fails after the core operation has already succeeded.
- A hook attempts to mutate supplied context data.
- A host registers no hooks.
- A portability preview succeeds while a later write import fails because target state changed.
- Existing lifecycle errors such as stale base versions, missing resources, invalid activation versions, invalid snapshots, or divergent import collisions occur while hooks are registered.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The slice adds explicit lifecycle hook points and deterministic invocation only. Recipes, workflow engines, live sync, background jobs, runtime scanning, package discovery, and policy frameworks remain out of scope.
- **Explicitness**: Hosts opt in by explicitly registering hooks. No hook is discovered through naming conventions, assembly scanning, attributes, or hidden provider behavior.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, background process, or external service changes are required. Local debugging remains straightforward because hook order and failure behavior are deterministic and visible.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow host applications to explicitly register lifecycle hooks.
- **FR-002**: The system MUST support before and after hook points for resource save operations, including create, update, and explicit schema-upgrade saves.
- **FR-003**: The system MUST support before and after hook points for resource activation.
- **FR-004**: The system MUST support before and after hook points for resource deactivation.
- **FR-005**: The system MUST support before and after hook points for snapshot export.
- **FR-006**: The system MUST support before and after hook points for import preview.
- **FR-007**: The system MUST support before and after hook points for write import.
- **FR-008**: The system MUST execute multiple hooks for the same lifecycle point in deterministic registration order.
- **FR-009**: The system MUST pass lifecycle-specific context to hooks, including operation kind, relevant resource or snapshot identity, cancellation state, and operation-specific options.
- **FR-010**: The system MUST allow before hooks to reject an operation before the underlying write or export occurs.
- **FR-011**: The system MUST NOT apply a save, activation, deactivation, export, preview, or write-import operation when a before hook rejects it.
- **FR-012**: The system MUST surface hook rejections and hook failures as clear structured failures or diagnostics appropriate to the operation being performed.
- **FR-013**: The system MUST NOT run after-success hooks when the underlying operation does not complete successfully.
- **FR-014**: The system MUST make after-hook failures visible without pretending that the already-completed operation was rolled back.
- **FR-015**: The system MUST respect cancellation while running hooks and MUST stop invoking later hooks after cancellation is observed.
- **FR-016**: The system MUST preserve existing behavior when no hooks are registered.
- **FR-017**: The system MUST keep hook context immutable or otherwise protect operation state from accidental mutation by observers.
- **FR-018**: The system MUST NOT introduce recipes, runtime scanning, live synchronization, background job orchestration, provider registries, public SQL, or public queryable resource surfaces.

### Key Entities *(include if feature involves data)*

- **Lifecycle Hook Registration**: A host-provided declaration that opts into one or more lifecycle points and participates in deterministic ordering.
- **Lifecycle Hook Context**: Operation-specific data supplied to a hook, such as save kind, resource identity, activation channel, portability snapshot, import options, planned counts, identity mappings, diagnostics, or cancellation state.
- **Lifecycle Hook Outcome**: The hook result indicating continuation, rejection, or failure, including structured diagnostic details when applicable.
- **Lifecycle Point**: A named moment in a resource or portability operation, such as before-save, after-save, before-activation, after-activation, before-export, or after-import.

### Assumptions

- Save hooks cover resource create, resource update, and explicit schema upgrade because all three append resource versions.
- After hooks are success observers and do not imply transactional rollback of already-completed operations.
- Hook registration order is the default execution order unless a later planning step introduces an explicit ordering model.
- Hook behavior is SDK-local and synchronous with the triggering operation; durable event delivery and retries belong to later operational slices.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can register two hooks for the same lifecycle point and observe them running in the same order across at least five repeated operations.
- **SC-002**: A host can block a save, activation, deactivation, export, preview import, or write import before mutation, and verification shows no underlying state change occurred.
- **SC-003**: A host can observe successful save, activation, deactivation, export, preview import, and write import operations with lifecycle-specific context sufficient to audit what changed.
- **SC-004**: Existing behavior with no hooks registered remains unchanged across the existing resource lifecycle, portability, query, and provider test suites.
- **SC-005**: Hook failures and cancellations produce deterministic, inspectable failure outcomes rather than silent skips or partial hook execution with ambiguous operation status.
