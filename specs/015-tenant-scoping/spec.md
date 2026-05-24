# Feature Specification: Tenant Scoping

**Feature Branch**: `015-tenant-scoping`  
**Created**: 2026-05-24  
**Status**: Draft  
**Input**: User description: "Add tenant-aware boundaries as the first Phase 5 slice. Hosts should be able to keep resource definitions, resources, activation state, queries, schema upgrades, portability snapshots, and lifecycle hooks isolated by an explicit tenant scope. The slice should stay small: explicit tenant context and scoped operations, clear default single-tenant behavior for existing apps, fail-closed behavior when a tenant-scoped operation is ambiguous, and no shared-definition inheritance, cross-tenant queries, policy engine, authorization system, migrations, runtime scanning, provider registry, public SQL, or IQueryable<Resource>."

## Clarifications

### Session 2026-05-24

- Q: How should tenant scope be selected for tenant-aware SDK operations? -> A: Each tenant-aware operation carries an explicit tenant scope; omitted scope means the documented default single-tenant scope.
- Q: How should tenant identifiers be compared and validated? -> A: Tenant identifiers are opaque exact-match values; the SDK rejects null, empty, or whitespace identifiers but does not case-fold or normalize.
- Q: How should tenant scope behave in portable snapshots and imports? -> A: Snapshots record source tenant identity; import writes only to one explicit target tenant and reports source/target tenant metadata.
- Q: What should omitted tenant scope mean after tenants are introduced? -> A: Omitted tenant scope always means the default single-tenant scope, even after tenant-specific data exists.
- Q: Should definition and resource identities be tenant-scoped or globally unique? -> A: Definition and resource identities are tenant-scoped; the same IDs may exist independently in different tenants.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Isolate Definitions And Resources By Tenant (Priority: P1)

As a host author, I need definitions and resources created for one tenant to be invisible to other tenants, so separate tenants can safely use the same resource types and names without accidental collisions.

**Why this priority**: Tenant isolation is the foundation for every later multi-tenant capability. Without it, policies, recipes, and operational hardening would sit on ambiguous data boundaries.

**Independent Test**: Create two tenants with the same resource type and resource names, then verify each tenant can create, update, read, and activate its own resources without seeing the other tenant's data.

**Acceptance Scenarios**:

1. **Given** two tenants define the same resource type, **When** each tenant creates resources for that type, **Then** each tenant sees only its own definitions and resources.
2. **Given** a resource exists in one tenant, **When** another tenant attempts to read, update, activate, or deactivate it, **Then** the operation fails as not found or out of scope without exposing the other tenant's data.
3. **Given** two tenants use the same definition or resource identifiers, **When** each tenant reads or mutates by identifier, **Then** each operation resolves only the data inside its effective tenant scope.
4. **Given** an existing single-tenant host does not provide a tenant scope, **When** it uses current definition and resource workflows, **Then** behavior remains equivalent to the current default environment regardless of whether other tenant-scoped data exists.

---

### User Story 2 - Query Within An Explicit Tenant Boundary (Priority: P2)

As a host author, I need queries to run against one effective tenant scope, so list views, lookup flows, and provider-backed searches cannot leak data across tenants.

**Why this priority**: Query leakage is the highest-risk read-path failure mode in a multi-tenant SDK.

**Independent Test**: Store matching resources in multiple tenants, run metadata, activation, and facet queries for one tenant, and verify results contain only resources from that tenant.

**Acceptance Scenarios**:

1. **Given** multiple tenants contain resources matching the same query, **When** the host queries within one tenant, **Then** only matching resources from that tenant are returned.
2. **Given** active versions exist in the same activation channel across tenants, **When** the host queries active resources in one tenant, **Then** activation state from other tenants has no effect on the result.
3. **Given** an operation requires a tenant and no effective tenant can be determined, **When** the host attempts the operation, **Then** the SDK fails closed with a stable diagnostic or exception.

---

### User Story 3 - Keep Integration Workflows Tenant-Scoped (Priority: P3)

As a host author, I need schema upgrades, portability, and lifecycle hooks to carry the effective tenant scope, so integration code can apply tenant-specific validation and avoid cross-tenant side effects.

**Why this priority**: These workflows span multiple records and are commonly used by host-level integrations. They must inherit the same isolation rules before higher-level recipes or policies are added.

**Independent Test**: Run schema upgrade, export, import preview, write import, and lifecycle hook flows for one tenant while another tenant contains similar data, then verify only the selected tenant participates.

**Acceptance Scenarios**:

1. **Given** a resource uses an older definition version in one tenant, **When** that tenant upgrades the resource schema, **Then** the upgrade resolves definition lineage only within that tenant.
2. **Given** a tenant exports resources, **When** the snapshot is created, **Then** it contains only the selected tenant's definitions, resources, versions, activation entries, and source tenant identity.
3. **Given** lifecycle hooks are registered, **When** a tenant-scoped operation runs, **Then** hook contexts identify the effective tenant and hooks cannot infer hidden cross-tenant state from the SDK workflow.

### Edge Cases

- A tenant identifier is empty, whitespace, or not in canonical form.
- A host mixes default single-tenant operations with explicit tenant-scoped operations in the same process; omitted tenant scope still targets only the default single-tenant scope.
- Two tenants use the same resource type, definition identifier, activation channel, and facet values.
- A query, schema upgrade, export, or import request omits tenant scope after tenant-scoped data exists.
- A portability import snapshot targets a tenant that already contains conflicting definitions or resources.
- Lifecycle hooks reject an operation for one tenant while another tenant has a similar operation in progress.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is one explicit effective tenant scope per operation plus a documented default single-tenant scope for existing hosts. Shared definition inheritance, tenant hierarchies, cross-tenant queries, policy engines, authorization, migrations, runtime scanning, provider registries, public SQL, and public `IQueryable<Resource>` are out of scope.
- **Explicitness**: Tenant scope must be visible through operation inputs, context, diagnostics, and documentation. The SDK must not discover tenants implicitly through naming conventions, ambient host context, ambient runtime scanning, or hidden provider state.
- **Dependencies**: None.
- **Operational Impact**: Existing local development and single-tenant usage remain unchanged. Multi-tenant hosts opt in through explicit scope selection and receive fail-closed diagnostics for ambiguous scoped operations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The SDK MUST define an explicit tenant scope concept with a stable tenant identity and a documented default single-tenant scope.
- **FR-001a**: Tenant-aware SDK operations MUST select tenant scope from explicit operation input; when no tenant scope is supplied, the operation MUST use the documented default single-tenant scope.
- **FR-001b**: Omitted tenant scope MUST always target the documented default single-tenant scope, including after explicit tenant-specific data exists.
- **FR-002**: Definition registration, lookup, and uniqueness checks MUST operate within the effective tenant scope.
- **FR-003**: Resource create, update, read, activation, and deactivation operations MUST operate within the effective tenant scope.
- **FR-003a**: Definition identities and resource identities MUST be unique only within their effective tenant scope; the same identifiers MAY exist independently in different tenants.
- **FR-004**: Query operations MUST evaluate against one effective tenant scope and MUST NOT return resources from any other tenant.
- **FR-005**: Schema status and schema upgrade workflows MUST resolve definition lineage within the effective tenant scope.
- **FR-006**: Portability export, import preview, and write import workflows MUST be tenant-scoped and MUST NOT read or write data from another tenant unless a future feature explicitly introduces cross-tenant behavior.
- **FR-006a**: Portable snapshots MUST record the source tenant identity. Import preview and write import MUST write only to one explicit target tenant and MUST report source and target tenant metadata.
- **FR-006b**: Portable snapshots MUST NOT contain multiple source tenants, and imports MUST NOT remap multiple source tenants to multiple target tenants in this feature.
- **FR-007**: Lifecycle hook contexts for tenant-scoped operations MUST expose the effective tenant scope.
- **FR-008**: Existing single-tenant hosts MUST continue to work without requiring new tenant input when they use the documented default single-tenant scope.
- **FR-009**: Tenant-scoped operations MUST fail closed with stable diagnostics or exceptions when the effective tenant scope is missing, ambiguous, invalid, or mismatched with the target data.
- **FR-010**: Tenant identifiers MUST be validated consistently before data is read or written.
- **FR-010a**: Tenant identifiers MUST be treated as opaque exact-match values. The SDK MUST reject null, empty, or whitespace tenant identifiers and MUST NOT case-fold, trim into a different identity, slugify, or otherwise normalize valid tenant identifiers.
- **FR-011**: Documentation MUST explain default single-tenant behavior, explicit tenant usage, fail-closed troubleshooting, query isolation, portability isolation, and lifecycle hook tenant context.
- **FR-012**: The feature MUST NOT introduce shared-definition inheritance, tenant hierarchy, cross-tenant queries, authorization or permission checks, policy engines, retention rules, migrations, runtime scanning, provider registries, public SQL, or public `IQueryable<Resource>`.

### Key Entities

- **Tenant Scope**: The effective boundary for definitions, resources, activation state, queries, schema upgrades, portability operations, and lifecycle hooks. It includes a stable tenant identity and may represent the default single-tenant environment. Tenant identities are opaque exact-match values.
- **Tenant-Scoped Definition**: A resource definition whose identity, uniqueness, lookup, and version lineage are evaluated within a tenant scope.
- **Tenant-Scoped Resource**: A resource and its versions whose identity, lifecycle operations, and activation records are evaluated within a tenant scope.
- **Tenant-Scoped Operation**: Any SDK workflow that reads, writes, queries, upgrades, exports, imports, or invokes hooks using one effective tenant scope.
- **Tenant-Scoped Snapshot**: A portable snapshot containing data from exactly one source tenant plus metadata that identifies that source tenant. Import workflows write it to exactly one explicit target tenant.
- **Tenant Scope Failure**: A stable failure condition reported when a requested operation has no valid tenant boundary or targets data outside the effective tenant scope.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can create the same definition and resource identifiers in two tenants and complete create, update, activate, read, and query workflows for each tenant with zero cross-tenant results.
- **SC-002**: Existing single-tenant quickstart flows and regression tests continue to pass without tenant-specific changes.
- **SC-003**: All missing, invalid, ambiguous, and mismatched tenant-scope cases produce stable failures that can be asserted by automated tests.
- **SC-004**: Tenant-scoped export/import workflows preserve tenant isolation for definitions, resources, versions, activation state, source tenant metadata, and target tenant metadata across round trips.
- **SC-005**: Lifecycle hook tests can assert the effective tenant scope for save, activation, deactivation, schema upgrade, export, preview import, and write import operations.

## Assumptions

- The first slice uses a default single-tenant scope to preserve existing host behavior.
- Tenant authorization and user permissions are host responsibilities and are not part of this feature.
- Shared definitions, tenant inheritance, and cross-tenant administrative queries require separate specifications.
- Provider storage changes, if needed, are limited to preserving the tenant boundary for current in-memory and SQLite JSON behavior; migration policy is out of scope for this slice.
