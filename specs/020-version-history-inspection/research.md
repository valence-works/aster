# Research: Resource Version History Inspection

## Dedicated History Service

Decision: Add a dedicated host-facing `IResourceVersionHistoryService` for one-resource history inspection.

Rationale: Existing `IResourceManager` can read versions, but hosts need a single SDK call that combines version snapshots with active channels, lifecycle marker state, tenant scope, and conservative maintenance hints. Keeping this separate from policy services prevents read-only history display from becoming another policy workflow.

Alternatives considered:

- Extend `IResourceManager`: rejected because the manager already owns create/update/activation operations, and history summaries combine state from multiple stores.
- Use query service projections: rejected because history is not a filter/sort query and must not introduce query planning or public SQL.
- Add a general reporting framework: rejected as too broad for the current slice.

## Activation State Enumeration

Decision: Add a narrow provider-facing activation-state reader that returns activation states for supplied resource IDs in one tenant.

Rationale: Existing active-version reads require the caller to know a channel name. History inspection needs the inverse: enumerate the channels that contain each version. A small reader over existing activation storage is the simplest explicit contract for this need.

Alternatives considered:

- Add channel enumeration to `IResourceManager`: rejected because it would mix management operations with provider state reads and still need a provider contract underneath.
- Reuse portability snapshots: rejected because portability reads are broader than one-resource history and carry import/export semantics not needed here.
- Infer channels from resource payloads: rejected because activation is explicitly decoupled from payloads.

## Maintenance Hints

Decision: Version summaries expose conservative maintenance hints: latest or active versions are protected from destructive pruning; historical inactive non-latest versions are possible maintenance candidates.

Rationale: Hosts benefit from obvious safety signals in version history screens. Policy eligibility still belongs to policy evaluation/application services because policies can include retention counts, lifecycle criteria, and stale-state checks.

Alternatives considered:

- Run policy evaluation from history inspection: rejected because it couples a read-only version timeline to policy workflows and broadens scope.
- Return no hints: rejected because latest/active protection is already a core invariant and useful to expose.
- Return definitive prune eligibility: rejected because retained-version and current-policy validation require policy-specific evaluation.

## Missing Resources And Tenant Boundaries

Decision: Missing resources return an empty history result for the effective tenant.

Rationale: Returning an empty result is safe for host screens and avoids leaking whether the same resource identifier exists in another tenant. Invalid request shape still follows existing argument validation patterns.

Alternatives considered:

- Throw not-found exceptions: rejected because existing read paths generally return empty or null for missing resources.
- Return cross-tenant diagnostics: rejected because tenant boundaries must remain explicit and non-leaky.

## SQLite Provider Behavior

Decision: Implement activation-state reads in the SQLite JSON provider over existing `activation_states` rows without schema changes.

Rationale: Activation states are already stored by tenant, resource, and channel with serialized payloads. Reading those rows for one resource is sufficient for history inspection and avoids migrations or physical indexes.

Alternatives considered:

- Add a new history table or materialized view: rejected because history can be assembled from existing state.
- Add public raw SQL hooks: rejected by query and provider guardrails.
- Use JSON query planning for all history fields: rejected because resource versions are already read through `IResourceVersionReader`.

## Out-Of-Scope Boundaries

Decision: The feature adds no mutation behavior, scheduler, automatic policy execution, provider registry, runtime scanning, public SQL, public queryable resource surface, storage migration, or broad workflow/state-machine infrastructure.

Rationale: The current product need is inspection. Keeping the slice read-only preserves operational simplicity and makes it a useful building block for hosts without changing maintenance semantics.

Alternatives considered:

- Add audit log persistence: deferred because it requires separate retention and storage decisions.
- Add cross-resource history reporting: deferred because this slice is intentionally bounded to one resource.
- Add policy-aware action recommendations: deferred to a future reporting/audit spec if hosts need it.
