# Research: Batch Version History Inspection

## Existing Service Extension

Decision: Extend `IResourceVersionHistoryService` with a batch inspection method rather than introducing a separate batch service.

Rationale: The existing service already owns history semantics: version ordering, latest detection, draft detection, active channels, lifecycle marker state, and maintenance hints. Adding a batch method keeps related behavior discoverable in one contract and avoids a second abstraction with the same collaborators.

Alternatives considered:

- Add a new `IResourceVersionBatchHistoryService`: rejected because it duplicates the existing service responsibility without a separate lifecycle or implementation need.
- Add a query service projection: rejected because history inspection is not a query-planner feature and must not expose public SQL or `IQueryable<Resource>`.
- Add provider-specific batch APIs: rejected because existing version/activation/marker abstractions can already satisfy the bounded read.

## Request Shape

Decision: Add `ResourceVersionHistoryBatchRequest` with optional tenant scope and explicit resource identifier collection.

Rationale: The host chooses the selected resources. Keeping the selection explicit avoids runtime scanning, discovery, search semantics, paging, and provider-specific query behavior.

Alternatives considered:

- Accept a filter expression: rejected as query-planner scope.
- Accept all resources in a tenant: rejected as too broad and potentially expensive.
- Add paging or sorting controls: rejected because the batch order should reflect explicit caller selection and histories already define version ordering.

## Identifier Normalization

Decision: Collapse duplicate resource identifiers using ordinal comparison while preserving the first occurrence. Empty selections return an empty result; blank identifiers fail fast.

Rationale: Duplicate IDs are common when callers merge UI selections. First-seen distinct ordering is deterministic and easy to reason about. Blank identifiers are invalid request shape, consistent with the existing single-resource API.

Alternatives considered:

- Return duplicate histories: rejected because it creates unnecessary repeated work and ambiguous result interpretation.
- Sort resource identifiers: rejected because callers expect selected resource order to be preserved.
- Ignore blank identifiers: rejected because silently dropping invalid IDs hides caller bugs.

## Missing Resources

Decision: Return an empty history for each distinct requested identifier with no stored versions in the effective tenant.

Rationale: This preserves existing single-resource behavior and lets callers correlate every requested identifier with a result without special provider-specific not-found handling.

Alternatives considered:

- Exclude missing resources: rejected because callers would need to compute missing IDs themselves.
- Throw not-found exceptions: rejected because existing history inspection returns an empty history for missing resources.
- Return diagnostics: rejected as unnecessary for a predictable non-error condition.

## Provider And Storage Scope

Decision: Reuse existing `IResourceVersionReader`, `IResourceActivationStateReader`, and `IResourceLifecycleMarkerStore` abstractions without adding storage, indexes, migrations, provider registries, or provider-specific batch contracts.

Rationale: Resource versions can already be read for a set of resource IDs in one tenant. Activation states can already be read for resource IDs in one tenant. Lifecycle markers can be read per resource. A simple core service orchestration satisfies the current requirement.

Alternatives considered:

- Add a materialized history table: rejected because history is derivable from existing state and would introduce synchronization concerns.
- Add provider-side history composition: rejected because the current need is provider-agnostic SDK semantics.
- Add physical indexes: rejected because the slice does not introduce new storage requirements; performance hardening can be a future operational slice if measured.

## Out-Of-Scope Boundaries

Decision: Do not introduce mutation behavior, policy evaluation, audit persistence, reporting infrastructure, public SQL, public `IQueryable<Resource>`, query planning, runtime scanning, provider registry, or automatic discovery.

Rationale: The feature is a bounded read convenience over existing history inspection. Broader reporting, audit, or query capabilities need separate product and storage decisions.

Alternatives considered:

- Policy-aware recommendations: deferred because policy evaluation already has dedicated preview/application surfaces.
- Cross-tenant administration: deferred because tenant administration needs separate boundary design.
- Operational benchmark suite: deferred as a later hardening slice.
