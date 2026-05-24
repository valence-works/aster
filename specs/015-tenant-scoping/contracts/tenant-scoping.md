# Contract: Tenant Scoping

This contract describes public SDK behavior for tenant-aware boundaries. Names are provisional for planning, but the behavior is normative.

## Tenant Scope Model

The SDK MUST expose a small tenant scope value that can be supplied to tenant-aware operations.

Required behavior:

- A missing tenant scope MUST resolve to the documented default single-tenant scope.
- Explicit tenant IDs MUST be opaque exact-match values.
- The SDK MUST reject null, empty, or whitespace tenant IDs.
- The SDK MUST NOT case-fold, slugify, trim into a different identity, or otherwise normalize valid tenant IDs.
- Tenant authorization and tenant existence checks are host responsibilities.

Public API shape:

- Existing request DTOs SHOULD gain optional tenant scope properties.
- Existing method-style APIs without request DTOs SHOULD gain additive tenant-aware overloads.
- Existing methods without tenant input MUST continue to target the default single-tenant scope.

## Definition Store Behavior

Definition registration and lookup MUST be scoped by the effective tenant.

Required behavior:

- Registering a definition appends a new version inside the effective tenant scope.
- Looking up the latest definition by ID returns the latest version for that ID in the effective tenant only.
- Looking up a specific definition version resolves `(tenant, definition ID, version)`.
- Listing definitions returns latest definitions for the effective tenant only.
- The same definition ID and version MAY exist in different tenants.

Existing no-tenant calls MUST continue to operate on the default single-tenant scope.

SQLite provider compatibility:

- Fresh SQLite databases MUST store tenant metadata for definitions, resources, and activation state.
- Existing pre-tenant SQLite databases MUST be treated as default-scope data after provider initialization.
- The provider MUST NOT introduce a general migration framework for this slice.

## Resource Manager Behavior

Resource lifecycle operations MUST resolve an effective tenant before reading or writing data.

Required behavior:

- Create, update, get latest, get version, get versions, activate, deactivate, and get active versions operate inside one effective tenant.
- Caller-supplied duplicate resource ID checks are tenant-scoped.
- Optimistic concurrency checks are tenant-scoped.
- A resource ID that exists in another tenant MUST behave as not found or out of scope for the current tenant.
- The same resource ID MAY exist in different tenants.

Existing no-tenant calls MUST continue to operate on the default single-tenant scope.

## Query Behavior

Portable queries MUST include or resolve one effective tenant scope.

Required behavior:

- Query execution filters by tenant before applying version scope, activation channel, predicates, sorting, skip, or take.
- Provider capability declarations do not need a new provider registry or negotiation model.
- Query validation MUST reject invalid tenant scope before execution.
- Results MUST NOT include resources from another tenant.

## Schema Upgrade Behavior

Schema status and schema upgrade workflows MUST resolve definition lineage within the effective tenant.

Required behavior:

- Schema status compares the resource's recorded definition version to definitions in the same tenant.
- Upgrade targets must exist in the same tenant.
- Upgrade writes append a new resource version in the same tenant.
- Cross-tenant definition lineage MUST fail closed.

## Portability Behavior

Export, validation, preview import, and write import MUST preserve tenant boundaries.

Required behavior:

- Export requests select data from exactly one effective tenant.
- Snapshots MUST record source tenant identity.
- Snapshots MUST NOT contain multiple source tenants.
- Preview import and write import MUST target exactly one explicit tenant.
- Import results and previews MUST report source and target tenant metadata.
- Multi-tenant remapping is out of scope.

## Lifecycle Hook Behavior

Lifecycle hook contexts MUST expose the effective tenant scope for tenant-scoped operations.

Required behavior:

- Save, activation, deactivation, schema upgrade, export, preview import, and write import contexts include tenant scope.
- Hooks observe the same effective tenant used by the underlying operation.
- Hook rejection/failure semantics remain unchanged and apply only to the current tenant-scoped operation.

## Compatibility

The default single-tenant scope preserves existing behavior:

- Existing callers that omit tenant scope use the default scope.
- Existing tests and quickstart flows must not require tenant-specific input.
- Adding explicit tenant-specific data must not change behavior of omitted-scope calls.

## Out Of Scope

This feature MUST NOT introduce:

- shared-definition inheritance;
- tenant hierarchy;
- cross-tenant queries;
- authorization or permission checks;
- policy engines or retention rules;
- migration engine or migration policy;
- runtime scanning;
- provider registry;
- public raw SQL;
- public `IQueryable<Resource>`.
