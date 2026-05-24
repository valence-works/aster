# Data Model: Tenant Scoping

## Tenant Scope

Represents the effective boundary for tenant-aware SDK operations.

Fields:

- `TenantId`: Stable opaque tenant identifier.
- `IsDefault`: Whether the scope represents the documented default single-tenant environment.

Validation rules:

- Tenant IDs must not be null, empty, or whitespace.
- Valid tenant IDs are exact-match opaque values.
- The SDK must not case-fold, trim into a different identity, slugify, or normalize valid tenant IDs.
- Omitted tenant scope resolves to the default single-tenant scope.

Relationships:

- Applied to definition, resource, activation, query, schema upgrade, portability, and lifecycle hook operations.

## Tenant-Scoped Definition

Represents a resource definition resolved within one tenant scope.

Fields:

- `TenantScope`: Effective tenant boundary.
- `DefinitionId`: Logical definition identifier.
- `Version`: Definition version number.
- Existing definition payload and metadata.

Validation rules:

- `(TenantScope, DefinitionId, Version)` is unique.
- Latest-definition lookup is scoped by `(TenantScope, DefinitionId)`.
- The same `DefinitionId` and `Version` may exist independently in different tenants.

Relationships:

- Tenant-scoped resources reference definition lineage inside the same tenant scope.

## Tenant-Scoped Resource

Represents a resource and its immutable versions resolved within one tenant scope.

Fields:

- `TenantScope`: Effective tenant boundary.
- `ResourceId`: Logical resource identifier.
- `Version`: Resource version number.
- `DefinitionId`: Logical definition identifier inside the same tenant.
- `DefinitionVersion`: Recorded definition version inside the same tenant.
- Existing resource payload, aspects, metadata, and version fields.

Validation rules:

- `(TenantScope, ResourceId, Version)` is unique.
- Caller-supplied duplicate `ResourceId` checks are scoped to the tenant.
- Resource reads, updates, activations, and deactivations must not resolve resources from another tenant.
- Existing optimistic concurrency remains scoped to the tenant's latest version for the resource.

Relationships:

- Activation state is resolved by tenant, resource ID, and channel.
- Queries use tenant scope before applying filters, sorting, and paging.

## Tenant-Scoped Activation State

Represents channel activation records for one tenant-scoped resource.

Fields:

- `TenantScope`: Effective tenant boundary.
- `ResourceId`: Logical resource identifier.
- `Channel`: Activation channel.
- Existing activation entries.

Validation rules:

- Activation and deactivation only affect records in the effective tenant.
- The same `ResourceId` and `Channel` may exist independently in different tenants.

## Tenant-Scoped Query

Represents a portable resource query evaluated within one tenant boundary.

Fields:

- `TenantScope`: Optional explicit tenant scope; omitted means default single-tenant scope.
- Existing query scope, activation channel, definition filter, predicates, sorting, and paging.

Validation rules:

- Query execution must resolve one effective tenant before reading candidate versions.
- Query results must not include resources from another tenant.
- Active-scope queries must apply tenant filtering before channel activation matching.

## Tenant-Scoped Snapshot

Represents a portable snapshot containing data from exactly one source tenant.

Fields:

- `SourceTenantScope`: Tenant identity recorded when the snapshot is exported.
- Existing snapshot format version.
- Tenant-scoped definition versions.
- Tenant-scoped resource versions.
- Tenant-scoped activation state.

Validation rules:

- A snapshot must contain data from exactly one source tenant.
- Export requests use the effective tenant scope to select data.
- Import preview and write import target exactly one explicit target tenant.
- Imports report source and target tenant metadata.
- Multi-source snapshot export and multi-target remapping are out of scope.

## Tenant Scope Failure

Represents fail-closed tenant-scope validation or mismatch.

Fields:

- `Code`: Stable failure code.
- `Message`: Human-readable explanation.
- `TenantId`: Tenant involved when available.
- `TargetTenantId`: Import target tenant when applicable.

Candidate codes:

- `tenant-scope-invalid`
- `tenant-scope-mismatch`
- `tenant-scope-required`
- `tenant-snapshot-multiple-tenants`

Validation rules:

- Invalid tenant IDs fail before data is read or written.
- Cross-tenant target mismatches fail without exposing other-tenant payload data.

## State Transitions

```text
No tenant input
  -> Resolve default tenant scope
  -> Execute operation in default scope

Explicit tenant input
  -> Validate tenant identifier
  -> Resolve explicit tenant scope
  -> Execute operation in explicit scope

Export
  -> Resolve effective tenant
  -> Select scoped data
  -> Emit snapshot with SourceTenantScope

Import preview/write
  -> Validate snapshot source tenant
  -> Resolve explicit target tenant
  -> Preview or write only within target tenant
```
