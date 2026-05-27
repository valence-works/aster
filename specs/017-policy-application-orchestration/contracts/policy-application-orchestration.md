# Contract: Policy Application Orchestration

This contract describes public SDK behavior for host-controlled application of policy preview outcomes. Names are provisional for planning, but behavior is normative.

## Application Service Behavior

The SDK MUST expose host-controlled policy application behavior.

Required behavior:

- The public service contract MUST use an async API with `CancellationToken`, shaped as `ValueTask<ResourcePolicyApplicationResult> ApplyAsync(ResourcePolicyApplicationRequest request, CancellationToken cancellationToken = default)`.
- Hosts MUST explicitly submit selected policy application candidates.
- Application MUST NOT run automatically from policy declarations or policy previews.
- Application MUST resolve exactly one effective tenant per request.
- Application MUST return one result per submitted candidate.
- Application MUST preserve input order in per-candidate results.
- Application MUST allow unrelated valid candidates to apply even when other candidates fail.
- Application MUST use existing lifecycle marker semantics for supported archive and soft-delete outcomes.

Out of scope:

- Background schedulers.
- Hidden retention jobs.
- Automatic policy execution.
- Authorization or permission policy engines.
- Runtime scanning.
- Provider registries.
- Public SQL.
- Public `IQueryable<Resource>`.

## Candidate Validation Behavior

Application candidates MUST be validated before writes.

Required behavior:

- Missing policy identity MUST fail with a stable invalid-candidate diagnostic.
- Missing resource identity MUST fail with a stable invalid-candidate diagnostic.
- Unsupported outcomes MUST fail with a stable unsupported-outcome diagnostic.
- Prune-preview outcomes MUST fail with `policy-pruning-preview-only`.
- Retain outcomes MUST NOT write lifecycle markers.
- Archive and soft-delete outcomes are the only supported write-side outcomes.
- Candidates with a supplied resource version MUST fail as stale when that version is no longer latest in the effective tenant.
- Candidates MUST fail when the referenced policy declaration is missing from the current resource definition.
- Candidates MUST fail when the referenced policy declaration exists but no longer matches the requested lifecycle outcome.

## Conflict Behavior

Application MUST handle conflicts deterministically.

Required behavior:

- Same-resource candidates with conflicting archive and soft-delete outcomes in one request MUST all fail for that resource before either marker is applied.
- Same-resource candidates with the same lifecycle outcome MUST NOT create duplicate marker state.
- Existing archived state plus a submitted soft-delete candidate MUST fail through lifecycle marker conflict behavior.
- Existing soft-deleted state plus a submitted archive candidate MUST fail through lifecycle marker conflict behavior.
- Reapplying the same existing marker state MUST return an already-satisfied result.

## Result Behavior

Application results MUST be structured for operator reporting and retry.

Required behavior:

- Every input candidate MUST produce exactly one candidate result.
- Candidate result statuses MUST distinguish applied, already satisfied, skipped, and failed.
- Applied results MUST identify the effective marker that was written.
- Already-satisfied results MUST identify the existing effective marker.
- Failed results MUST include at least one stable diagnostic.
- Aggregate counts MUST match per-candidate statuses.
- Results MUST NOT include resources outside the effective tenant.

## Tenant Boundary Behavior

Application MUST enforce tenant boundaries.

Required behavior:

- Omitted tenant scope MUST resolve to the default single-tenant scope.
- Candidate resource lookup MUST be tenant-scoped.
- Policy declaration lookup MUST be tenant-scoped.
- Marker writes MUST be tenant-scoped.
- A candidate referencing a resource ID that exists only outside the effective tenant MUST fail as target-not-found and MUST NOT write a marker.
- Application MUST NOT perform cross-tenant existence checks solely to produce a distinct tenant-boundary diagnostic.

## Compatibility Behavior

The feature MUST remain additive for existing hosts.

Required behavior:

- Existing policy preview behavior MUST remain non-mutating.
- Existing direct lifecycle marker writes MUST remain valid.
- Existing query, portability, activation, resource write, and schema upgrade behavior MUST remain deterministic.
- Existing lifecycle hook behavior MUST remain unchanged.
- Existing `AddAsterCore()` and `AddAsterSqliteJson()` registration behavior MUST remain compatible.

## Provider Behavior

Providers MUST NOT need new storage contracts for this slice.

Required behavior:

- In-memory behavior MUST work through existing core service registrations.
- SQLite JSON behavior MUST work through existing provider registrations that replace definition/version/marker services.
- Application MUST not require provider-specific executors or registries.
- Application MUST not add schema migration or storage provisioning requirements.

## Documentation Behavior

Documentation MUST explain:

- host-controlled application;
- selected candidate input;
- per-candidate statuses and diagnostics;
- idempotency;
- stale candidate rejection;
- policy declaration mismatch rejection;
- same-resource conflicting outcome rejection;
- tenant boundary behavior;
- pruning preview-only behavior;
- lifecycle hook non-goals;
- out-of-scope automatic execution and destructive pruning writes.
