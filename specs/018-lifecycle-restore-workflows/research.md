# Research: Lifecycle Restore Workflows

## Decision: Add A Small Lifecycle Restore Service

Decision: Add a focused host-facing lifecycle restore service for restore preview and restore application.

Rationale: Restore is related to lifecycle markers, but it has batch preview, batch application, per-candidate diagnostics, and expected-state validation that differ from direct single-marker writes. A small service keeps restore behavior cohesive and avoids broadening the existing marker apply contract.

Alternatives considered:

- Add methods to the existing lifecycle marker service: rejected because it would widen the direct marker write contract and make a single-resource apply service carry batch restore workflow behavior.
- Add a generic lifecycle workflow service: rejected because restore does not need a broad state-transition framework.
- Ask hosts to clear provider state directly: rejected because hosts need consistent validation, tenant scoping, idempotency, and diagnostics.

## Decision: Add A Narrow Marker Clear Capability

Decision: Add a narrow provider-facing capability for clearing the marker for a resource in a tenant.

Rationale: Current provider-facing storage can read and save effective marker state, but restore needs to remove marker state so lifecycle-state queries and portability see the resource as unmarked. Persisting `None` as a marker would create a second representation for the same state and complicate provider queries. A dedicated capability lets built-in providers support restore without changing read/write semantics.

Alternatives considered:

- Save a marker with `State = None`: rejected because absence already represents no marker and persisted none-state rows would make querying and portability ambiguous.
- Make restore service call provider-specific delete APIs: rejected because core must stay provider-agnostic.
- Reuse internal in-memory rollback helpers: rejected because SQLite and future providers need the same public provider-facing contract.

## Decision: Use Explicit Restore Candidates

Decision: Restore requests contain selected candidates with resource ID and expected lifecycle marker state.

Rationale: Expected state makes restore fail closed when an operator acts on stale UI data. It also lets hosts restore a subset of resources without hidden discovery or re-evaluation.

Alternatives considered:

- Accept only resource IDs: rejected because a resource might have changed from archived to soft-deleted between preview and application.
- Accept policy IDs from the original policy application: rejected because restore reverses marker state, not policy intent, and should not require policy declarations to still exist.
- Restore all currently marked resources: rejected because it would violate explicit host intent.

## Decision: Provide Preview And Application Workflows

Decision: Restore preview is non-mutating and reports candidate outcomes; restore application revalidates current state and clears markers when the expected state matches.

Rationale: Preview supports operator review, while application remains safe when marker state changes after preview. Revalidation during application prevents stale restore candidates from clearing unrelated current marker state.

Alternatives considered:

- Application only: rejected because hosts need non-mutating reporting before restore.
- Preview token that application trusts: rejected because marker state can change between preview and application and the current requirements do not need token infrastructure.
- All-or-nothing batch: rejected because unrelated valid restore candidates should still succeed when one candidate fails.

## Decision: Treat Missing Markers As Already Restored

Decision: A candidate for an existing resource with no lifecycle marker reports already restored.

Rationale: Restore is naturally idempotent. Retrying a restore after success or after an external clear should not fail solely because the marker is absent.

Alternatives considered:

- Fail missing markers: rejected because it makes safe retries noisy and does not reflect the desired end state.
- Skip missing markers without a status: rejected because every input must receive a deterministic result.

## Decision: Fail Marker-State Mismatches

Decision: If the current marker state differs from the candidate's expected state, preview and application fail that candidate with a stable marker-mismatch diagnostic.

Rationale: Clearing a different marker than the host expected can undo a newer operator decision. Fail-closed behavior preserves explicitness and prevents stale UI selections from clearing current lifecycle state.

Alternatives considered:

- Clear whatever marker exists: rejected because it can erase newer state accidentally.
- Treat mismatch as already restored: rejected because a marker still exists and the resource is not restored.
- Auto-transition between archive and soft-delete: rejected as a broader lifecycle state-machine feature.

## Decision: Preserve Lifecycle Hook Behavior

Decision: Restore workflows do not add lifecycle hook coverage in this slice.

Rationale: Existing hook contexts cover existing resource save, activation, schema, and portability workflows. Adding restore hooks would broaden public contracts before a demonstrated host need.

Alternatives considered:

- Invoke hooks around every marker clear: rejected because direct marker writes do not currently define a hook pipeline.
- Add before/after restore hooks: rejected as unnecessary infrastructure for this slice.

## Decision: Preserve Provider And Operational Simplicity

Decision: Restore remains core SDK behavior over existing abstractions with no provider registry, runtime scanning, scheduler, public SQL, public `IQueryable<Resource>`, storage migration, destructive pruning write, or authorization system.

Rationale: Restore is an explicit host operation over current marker state. Keeping it bounded preserves the architecture established by tenant scoping, policy foundations, and policy application orchestration.

Alternatives considered:

- Background restore runner: rejected because automatic execution is out of scope.
- Authorization-integrated restore: rejected because host authorization remains outside the SDK.
- Provider-specific restore executors: rejected because restore rules are provider-agnostic.
