# Research: Policy Pruning Application

## Host-Controlled Pruning Application

Decision: Add a dedicated host-facing pruning application service for selected version-pruning candidates.

Rationale: Existing policy application applies archive and soft-delete lifecycle markers. Version pruning is destructive and needs stronger safety preflight, separate statuses, and provider removal behavior. A dedicated service keeps destructive semantics discoverable and avoids broadening marker application.

Alternatives considered:

- Extend lifecycle marker application: rejected because pruning removes version snapshots rather than writing marker state.
- Add a general policy engine: rejected as too broad and contrary to explicit host control.
- Let hosts delete provider data directly: rejected because hosts need consistent tenant scoping, stale-candidate validation, and diagnostics.

## Provider-Facing Version Removal

Decision: Add a narrow provider-facing resource version pruning capability.

Rationale: Current writer contracts can save versions and update activation state, but cannot remove a version snapshot. A small capability is justified by the current destructive operation and keeps provider-specific storage details out of core policy logic.

Alternatives considered:

- Add delete methods to `IResourceVersionWriter`: rejected because normal writer semantics are append-only and pruning is an exceptional destructive capability.
- Reuse portability import/export to rewrite retained data: rejected because it is operationally heavier and risks changing snapshot semantics.
- Introduce provider registry/discovery: rejected because explicit DI registration already expresses provider capabilities.

## Preview-Derived Candidate Shape

Decision: Candidates use existing preview fields: policy ID, policy kind, prune-preview outcome, resource ID, and resource version. No opaque preview token is introduced.

Rationale: Existing preview outcomes already contain enough information to identify the selected policy and version. Revalidating current state at application time provides stale protection without creating a token lifecycle or persistence requirement.

Alternatives considered:

- Require opaque preview IDs: rejected because it would require additional persisted preview state or token generation.
- Require version-specific `Id` from preview: deferred because existing preview outcomes expose version number, and resource ID plus version is the current stable host-facing version identity.

## Safety Preflight

Decision: Application revalidates current resource existence, version existence, current policy declaration, current policy criteria match, latest status, activation state, lifecycle marker criteria match, and retained-version safety before removal.

Rationale: A pruning preview can become stale after host review. Rechecking current state prevents deletion of a version that became latest, active, policy-incompatible, tenant-mismatched, or necessary to satisfy retained-version safety.

Alternatives considered:

- Trust preview results: rejected because stale destructive operations are unsafe.
- Lock resources between preview and application: rejected because it would add concurrency infrastructure outside current requirements.
- Require all candidates in a request to remain valid: rejected because partial success is already part of the result model and is more useful for hosts.

## Retained-Version Safety

Decision: Application must leave at least the current policy's maximum retained version count for the resource, and must never remove the latest or active versions.

Rationale: The pruning policy declares the retained-version floor. Applying the current floor protects against stale preview selections after new versions are created or policies are changed.

Alternatives considered:

- Use the retained count from the original preview: rejected because it can become stale if policy declarations change.
- Keep only one version unconditionally: rejected because policy-specific retained counts already exist and should remain authoritative.

## Duplicate And Already-Pruned Outcomes

Decision: Duplicate candidates in one request are deterministic. Reapplying a candidate for a version that is already absent returns already-pruned when the resource still exists and current policy identity is otherwise valid.

Rationale: Hosts may retry after partial failures or resubmit old selections. Idempotent already-pruned outcomes make retry behavior predictable without hiding invalid tenant or policy mismatches.

Alternatives considered:

- Treat missing candidate versions as failures: rejected because it makes safe retries noisy and less useful.
- Ignore duplicates entirely: rejected because every input candidate must receive one result.

## Transaction Semantics

Decision: Cross-candidate all-or-nothing behavior is not required. Providers should remove each valid candidate conditionally using current-state checks and return per-candidate outcomes.

Rationale: The feature already allows partial success. Requiring a cross-provider transaction abstraction would add infrastructure not needed for current product behavior.

Alternatives considered:

- Require all-or-nothing batches: rejected because it would force providers into a shared transaction model and conflict with partial-success requirements.
- Remove without current-state conditions: rejected because races could remove versions that became protected between preflight and write.

## Out-Of-Scope Boundaries

Decision: The feature adds no scheduler, automatic retention job, authorization engine, provider registry, runtime scanning, public SQL, public queryable resource surface, schema migration, or broad workflow/state-machine infrastructure.

Rationale: Pruning is explicit host-controlled maintenance over existing policy previews and provider abstractions. Keeping it bounded preserves the architecture established by tenant scoping, policy foundations, policy application, and lifecycle restore.

Alternatives considered:

- Automatic retention execution: rejected because scheduling, operator controls, and authorization need separate product decisions.
- General lifecycle workflow engine: rejected because current archive, soft-delete, restore, and pruning needs are covered by focused services.
