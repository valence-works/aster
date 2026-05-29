# Data Model: Policy Pruning Application

## Policy Pruning Application Request

Host-provided request for applying selected version-pruning candidates.

Fields:

- `TenantScope`: optional tenant scope; omitted means the default single-tenant scope.
- `Candidates`: ordered list of selected pruning candidates.
- `AppliedAt`: optional host timestamp for application reporting; it does not create resource history.

Validation rules:

- A null request is invalid.
- Null candidate lists are treated as empty.
- Empty candidates are valid and return an empty result.
- The effective tenant is resolved once per request.
- The request must not imply cross-tenant pruning.

## Policy Pruning Candidate

Host-selected version-pruning target derived from a policy preview candidate.

Fields:

- `PolicyId`: policy declaration identifier from the preview candidate.
- `PolicyKind`: must identify version pruning.
- `Outcome`: must be prune-preview.
- `ResourceId`: logical resource identifier.
- `ResourceVersion`: target version number from the preview candidate.

Validation rules:

- `PolicyId` is required and must not be empty or whitespace.
- `PolicyKind` must be version pruning.
- `Outcome` must be prune-preview.
- `ResourceId` is required and must not be empty or whitespace.
- `ResourceVersion` is required and must be greater than zero.
- Candidates are evaluated only inside the effective tenant.

## Policy Pruning Application Result

Write response for one pruning application request.

Fields:

- `TenantScope`: effective tenant used for application.
- `AppliedAt`: optional host timestamp supplied by the request.
- `Candidates`: ordered application candidate results, one per input candidate.
- Aggregate counts for pruned, already-pruned, skipped, and failed candidates.

Validation rules:

- Application allows partial success for unrelated candidates.
- Application returns exactly one result per input candidate.
- Application updates its in-memory view after a successful removal so duplicates are deterministic.

## Policy Pruning Candidate Result

Per-candidate outcome for pruning application.

Fields:

- `Index`: zero-based input index.
- `PolicyId`: input policy identifier when supplied.
- `ResourceId`: input resource identifier when supplied.
- `ResourceVersion`: input resource version when supplied.
- `Status`: candidate status.
- `Diagnostics`: stable diagnostics for failed candidates.

Statuses:

- `Pruned`: target version existed, passed safety preflight, and was removed.
- `AlreadyPruned`: target resource exists and the submitted version is already absent.
- `Skipped`: deterministic duplicate after a prior result where no additional write is needed.
- `Failed`: invalid shape, stale policy basis, missing resource, protected state, unsafe retained-version removal, provider unsupported behavior, or write failure.

## Policy Pruning Diagnostic

Stable per-candidate diagnostic.

Diagnostic needs:

- `policy-pruning-candidate-invalid`: missing policy identity, wrong kind/outcome, missing resource identity, or missing/invalid resource version.
- `policy-pruning-target-not-found`: target resource does not exist in the effective tenant.
- `policy-pruning-version-protected-latest`: target version is the current latest version for the resource.
- `policy-pruning-version-protected-active`: target version is active in at least one activation channel.
- `policy-pruning-policy-missing`: policy declaration no longer exists on the current definition.
- `policy-pruning-policy-mismatch`: current policy declaration no longer matches the submitted kind, outcome, or criteria needed for the target version.
- `policy-pruning-unsafe`: removing the target would violate the current retained-version safety floor.
- `policy-pruning-provider-unsupported`: active provider cannot remove resource versions.
- `policy-pruning-write-failed`: provider reported that a candidate passed preflight but could not be removed.

Diagnostic rules:

- Diagnostics must be stable enough for tests and host UI branching.
- Tenant-scoped misses use the same not-found behavior as existing lifecycle and policy workflows.
- Policy mismatch diagnostics identify the policy and target version when available.

## Provider Version Pruning Capability

Provider-facing storage capability for destructive removal of selected resource versions.

Fields/operation inputs:

- `ResourceId`: logical resource identifier.
- `ResourceVersion`: target version number.
- `TenantScope`: effective tenant.

Validation rules:

- Removal must affect only the supplied tenant, resource, and version.
- Removal must not remove definitions, lifecycle markers, activation state, policy declarations, or other versions.
- Removal should be conditional on the target version still existing.
- The operation returns whether a matching version was removed.

## State Transitions

```text
Historical inactive version
  -- apply pruning candidate that passes preflight --> Removed

Missing target version
  -- apply otherwise valid pruning candidate --> AlreadyPruned

Latest version
  -- apply pruning candidate --> Failed(protected latest)

Active version
  -- apply pruning candidate --> Failed(protected active)

Policy or criteria changed
  -- apply stale pruning candidate --> Failed(policy mismatch)

Unsafe retained-version floor
  -- apply pruning candidate --> Failed(unsafe)
```

Prohibited transitions:

- Pruning the latest version.
- Pruning any active version.
- Pruning every retained version for a resource.
- Pruning versions outside the effective tenant.
- Pruning archive or soft-delete marker state.
- Rewriting remaining resource versions.
- Changing activation state.
- Mutating lifecycle markers or policy declarations.
- Running pruning automatically without a host request.
