# Research: Historical Version Activation

## Existing API Versus New Promotion Service

Decision: Reuse existing `ActivateAsync` methods and allow the supplied version to be any existing resource version.

Rationale: The existing API already accepts an explicit resource version, channel, tenant scope, and `allowMultipleActive` flag. Adding a separate promotion service would duplicate activation behavior and create unnecessary conceptual surface.

Alternatives considered:

- Add `PromoteAsync`: rejected because it would wrap the same activation state write and require duplicate hooks/docs.
- Add request models: rejected because the current method arguments are already explicit.
- Keep latest-only activation: rejected because the roadmap requires historical promotion.

## Version Existence Validation

Decision: Keep the existing version-not-found behavior when the requested version does not exist in the effective tenant.

Rationale: Historical activation should broaden the allowed version set from "latest only" to "any existing version", not relax existence checks.

Alternatives considered:

- Let activation state reference missing versions: rejected because it can create dangling active state.
- Return no-op for missing versions: rejected because hosts need deterministic failure for invalid selections.

## Single-Active And Multi-Active Behavior

Decision: Preserve the current `allowMultipleActive` behavior for historical versions.

Rationale: The flag already controls whether activation replaces the channel set or appends to it. Historical versions should follow the same channel semantics as latest versions.

Alternatives considered:

- Always replace when activating historical versions: rejected because preview and staging channels may intentionally allow multiple active historical snapshots.
- Always append historical versions: rejected because published-style channels need single-active rollback semantics.

## Lifecycle Hooks

Decision: Invoke existing activation hooks for historical activation with the requested version and resulting active version list.

Rationale: Hooks are the host-visible way to observe/gate activation. Historical activation is still activation, so hooks should not be bypassed.

Alternatives considered:

- Add new hook points: rejected because this slice does not introduce a new operation type.
- Skip hooks for historical activation: rejected because it would hide activation writes from host behavior.

## Provider Storage

Decision: Use existing activation state writes through `IResourceVersionWriter`.

Rationale: Historical activation changes only which version number appears in activation state. In-memory and SQLite JSON stores already persist active version lists.

Alternatives considered:

- Add provider migration or activation metadata: rejected because no new data is required.
- Add provider-specific historical activation capability: rejected because existing writer contract already supports arbitrary active version lists.
