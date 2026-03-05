# Contract: Persistence & Querying Provider (Phase 2)

## Purpose
Define the external behavior contract for the SQLite reference provider while preserving `Aster.Core` provider-agnostic abstractions.

## Scope
- Persistence of definitions, resource versions, and activation state.
- Query execution for the portable `ResourceQuery` model.
- Infrastructure initialization/upgrade execution contract.

## Required Interface Surface

### 1) Write Contract
- `IResourceWriteStore.SaveVersionAsync(Resource, CancellationToken)`
- `IResourceWriteStore.UpdateActivationAsync(string resourceId, string channel, ActivationState state, CancellationToken)`

Behavioral guarantees:
- Save operations append new immutable versions only.
- Conflicting updates are rejected via typed optimistic concurrency errors.
- Activation updates are atomic per `(ResourceId, Channel)`.

### 2) Read/Manager Contract
- `IResourceManager` lifecycle and retrieval operations remain source of truth for public behavior.

Behavioral guarantees:
- `CreateAsync` honors singleton and duplicate-ID constraints.
- `UpdateAsync` requires matching `BaseVersion`.
- `ActivateAsync` enforces per-channel mode (`SingleActive` or `MultiActive`).

### 3) Query Contract
- `IResourceQueryService.QueryAsync(ResourceQuery, CancellationToken)`

Behavioral guarantees:
- Supported operators in Phase 2: `Equals`, `Contains`, `Range`.
- Paging and sorting are deterministic.
- Records missing sort field values remain in result set and sort last.
- Query failures return clear typed errors (unsupported operator/field, unavailable infrastructure, invalid request shape).

### 4) Definition Registry Contract
- `IResourceDefinitionStore` contracts remain immutable-version semantics.

Behavioral guarantees:
- Registering a definition appends a new version.
- Latest and point-in-time definition retrieval are supported.

## Infrastructure Step Contract

Provider implementation must expose an infrastructure runner with:
- `ApplyAsync(targetVersion?)`
- `GetPendingAsync()`
- `GetAppliedAsync()`
- Optional `VerifyAsync()` for readiness checks

Behavioral guarantees:
- Running on an empty database creates required structures.
- Re-running on a current database is idempotent (no destructive duplicates).
- Host can choose auto-run at startup or manual execution path.

## Error Contract

The provider must map failures to typed categories:
- `ConcurrencyConflict` (optimistic lock mismatch)
- `VersionNotFound`
- `InfrastructureUnavailable`
- `UnsupportedQueryFeature`
- `ValidationFailed`

Notes:
- Existing `Aster.Core.Exceptions` types are used where available.
- Provider-specific exceptions may exist internally but must be translated before crossing abstraction boundaries.

## Compatibility Rules
- No contract changes may leak provider-specific SQL or database types into `Aster.Core` public abstractions.
- Serialization format changes must preserve backward readability for previously persisted rows within the same provider major version.
