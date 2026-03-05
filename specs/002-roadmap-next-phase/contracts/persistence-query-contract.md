# Contract: Persistence & Querying Provider (Phase 2)

## Purpose
Define the external behavior contract for the SQLite reference provider while preserving `Aster.Core` provider-agnostic abstractions.

## Scope
- Persistence of `ResourceDefinitionRecord`, `ResourceRecord`, and `ActivationRecord` rows.
- Query execution for the portable `ResourceQuery` model.

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
- `CreateAsync` honors singleton and duplicate-ID constraints (resolved via `ResourceDefinitionRecord.IsSingleton`).
- `UpdateAsync` requires matching `BaseVersion` (optimistic lock).
- `ActivateAsync` accepts an optional `ChannelMode`; if supplied it sets or updates the stored per-channel mode, if omitted the stored mode is used. `SingleActive` enforces at-most-one active version; `MultiActive` allows many.

### 3) Query Contract
- `IResourceQueryService.QueryAsync(ResourceQuery, CancellationToken)`

Behavioral guarantees:
- Supported operators in Phase 2: `Equals`, `Contains`, `Range`.
- Sorting is deterministic; a tie-break on (`ResourceId`, `Version`) is always appended.
- Records missing sort field values remain in the result set and sort last.
- Query failures return clear typed errors (unsupported operator/field, invalid request shape).
- `ResourceQuery` is translated to parameterised SQL at execution time; no raw SQL is exposed through the abstraction.

### 4) Definition Registry Contract
- `IResourceDefinitionStore` contracts remain immutable-version semantics.

Behavioral guarantees:
- Registering a definition appends a new version.
- Latest and point-in-time definition retrieval are supported.

## Error Contract

The provider must map failures to typed categories:
- `ConcurrencyConflict` (optimistic lock mismatch)
- `VersionNotFound`
- `UnsupportedQueryFeature`
- `ValidationFailed`

Notes:
- Existing `Aster.Core.Exceptions` types are used where available.
- Provider-specific exceptions may exist internally but must be translated before crossing abstraction boundaries.

## Compatibility Rules
- No contract changes may leak provider-specific SQL or database types into `Aster.Core` public abstractions.
- The Phase 2 provider ships a single fixed schema version. In-place schema upgrades are out of scope; a breaking schema change requires a fresh database.
- Within Phase 2, the JSON serialisation format of all stored fields (`PayloadJson`, `AspectsJson`, `ActiveVersionsJson`) MUST NOT change in a way that breaks deserialisation of previously written rows.
