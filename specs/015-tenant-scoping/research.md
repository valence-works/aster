# Research: Tenant Scoping

## Decision: Explicit Request-Level Tenant Scope

**Decision**: Tenant-aware operations use explicit operation input to select tenant scope. If no tenant scope is supplied, the operation uses the documented default single-tenant scope.

**Rationale**: Explicit operation input is visible in code reviews, tests, diagnostics, and documentation. It avoids hidden ambient state becoming the isolation boundary and keeps existing no-tenant callers compatible.

**Alternatives considered**:

- Ambient host context: rejected because it hides tenant selection and makes background jobs/tests harder to reason about.
- Hybrid ambient fallback: rejected for this slice because it introduces precedence rules before there is a demonstrated need.

## Decision: Default Single-Tenant Scope Is Always Valid

**Decision**: Omitted tenant scope always maps to the default single-tenant scope, even after explicit tenant-scoped data exists.

**Rationale**: This keeps the feature additive for existing SDK consumers. Compatibility should not depend on whether another part of the process has started using tenants.

**Alternatives considered**:

- Fail omitted scope after tenant-specific data exists: rejected because behavior would depend on mutable store state.
- Require tenant scope for all writes: rejected because it breaks the current quickstart and existing tests without adding isolation value for default-scope hosts.

## Decision: Opaque Exact-Match Tenant Identifiers

**Decision**: Tenant identifiers are opaque exact-match values. The SDK rejects null, empty, or whitespace tenant identifiers but does not case-fold, slugify, trim into a different identity, or normalize valid identifiers.

**Rationale**: Hosts often already own tenant identity rules. Exact matching avoids hidden normalization collisions and keeps the SDK provider-agnostic.

**Alternatives considered**:

- Case-insensitive tenant IDs: rejected because it assumes host identity semantics.
- Restricted slug tenant IDs: rejected because it creates unnecessary host coupling.

## Decision: Definition And Resource Identities Are Tenant-Scoped

**Decision**: Definition and resource identifiers are unique only within their effective tenant scope. The same IDs may exist independently in different tenants.

**Rationale**: This is the clearest isolation model and enables environment import/export workflows where identical IDs can exist in different tenant boundaries.

**Alternatives considered**:

- Globally unique resource IDs with tenant-scoped definitions: rejected because it creates inconsistent identity rules.
- Globally unique all IDs with tenant filtering: rejected because it prevents true tenant independence and complicates import conflict behavior.

## Decision: Single-Source Snapshot, Single-Target Import

**Decision**: Portable snapshots record one source tenant identity. Import preview and write import target one explicit tenant and report source/target tenant metadata.

**Rationale**: This preserves auditability and tenant isolation without introducing multi-tenant remapping or recipe execution.

**Alternatives considered**:

- Tenant-neutral snapshots: rejected because source tenant identity is useful diagnostic metadata.
- Multi-tenant snapshots with remapping: rejected as a future recipe/admin feature with larger acceptance criteria.

## Decision: Central Tenant Scope Validation

**Decision**: Add a small central tenant scope validation/resolution path used by resource manager, query, schema upgrade, portability, and provider-facing requests.

**Rationale**: The validation rules are intentionally small but cross-cutting. Centralizing null/empty/default resolution prevents each service/provider from inventing slightly different rules.

**Alternatives considered**:

- Inline validation everywhere: rejected because it risks inconsistent failure behavior.
- Tenant registry service: rejected because the slice does not create, discover, or authorize tenants.

## Decision: Additive API Shape

**Decision**: Request DTOs that already exist gain optional tenant scope properties. Method-style APIs without request DTOs gain additive tenant-aware overloads while existing methods continue to target the default single-tenant scope.

**Rationale**: This keeps the public API explicit without forcing a broad request-object refactor across established method-shaped contracts. Existing callers remain source-compatible, and tenant-aware callers can choose the scoped overloads or request fields.

**Alternatives considered**:

- Add tenant scope only through request DTOs: rejected because several current APIs have no request object and converting them would be a larger public API redesign.
- Add ambient tenant context: rejected because it hides tenant selection.
- Replace existing methods with tenant-required methods: rejected because it breaks default single-tenant compatibility.

## Decision: SQLite Default-Scope Compatibility Bootstrap

**Decision**: SQLite JSON initialization may perform minimal idempotent compatibility work for existing pre-tenant databases by adding tenant metadata and treating existing rows as default-scope data. This is not a general migration framework.

**Rationale**: The feature must preserve existing single-tenant hosts. Without a compatibility path, old SQLite files would either fail or leak into an undefined tenant boundary. Keeping this work in provider initialization preserves operational simplicity while avoiding a new migration policy.

**Alternatives considered**:

- Fresh databases only: rejected because it weakens the compatibility requirement for existing SQLite users.
- Full migration framework: rejected as a separate operational hardening topic.
- Runtime query fallback without persisted tenant metadata: rejected because it complicates every query and write path instead of making the storage boundary explicit.

## Decision: Provider-Specific Filtering Without New Infrastructure

**Decision**: In-memory and SQLite JSON providers add explicit tenant filtering/partitioning using the existing provider packages and test stack. The slice does not introduce migrations, a provider registry, a planner, or a provisioning framework.

**Rationale**: Tenant filtering is a storage/provider responsibility once core defines the scope. The existing provider boundaries are sufficient for this slice.

**Alternatives considered**:

- New provider registry/framework: rejected because one active provider remains the model.
- Migration/provisioning framework: rejected because migration policy is a separate operational hardening topic.
- Encode tenant behavior through query predicates only: rejected because definitions, activation, schema upgrades, and direct resource lookups also need tenant boundaries.
