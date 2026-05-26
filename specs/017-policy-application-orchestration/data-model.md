# Data Model: Policy Application Orchestration

## Policy Application Request

Represents one host-controlled request to apply selected policy preview outcomes.

Fields:

- `TenantScope`: Optional tenant scope; omitted means the default single-tenant scope.
- `Candidates`: Ordered collection of host-selected application candidates.
- `AppliedAt`: Host-supplied timestamp used for lifecycle marker writes.
- `Reason`: Optional host-visible reason applied to marker writes unless a candidate supplies a more specific reason.

Validation rules:

- The request resolves to exactly one effective tenant.
- Empty candidate collections are valid and produce an empty result.
- Application must not imply cross-tenant behavior.
- Application must not re-evaluate hidden policy scopes or discover policies automatically.

Relationships:

- Contains zero or more policy application candidates.
- Produces a policy application result.

## Policy Application Candidate

Represents one selected preview candidate that a host wants to apply.

Fields:

- `PolicyId`: Policy declaration identifier from the preview candidate.
- `PolicyKind`: Previewed policy kind.
- `Outcome`: Previewed outcome.
- `ResourceId`: Target logical resource identifier.
- `ResourceVersion`: Optional resource version from the preview candidate.
- `Reason`: Optional host-visible marker reason override.

Validation rules:

- `PolicyId`, `Outcome`, and `ResourceId` are required.
- Supported write outcomes are archive and soft-delete only.
- Prune-preview candidates are rejected with preview-only diagnostics.
- Retain or unknown outcomes are rejected as unsupported application outcomes.
- When `ResourceVersion` is supplied, it must still be the latest resource version in the effective tenant.
- The current resource definition for the latest target resource must contain the referenced policy declaration.
- The current policy declaration outcome must match the candidate lifecycle outcome.
- Candidates must not apply outside the effective tenant.

Relationships:

- Derived from a policy evaluation preview candidate.
- References one logical resource in one effective tenant.
- Produces one candidate application result.

## Policy Application Result

Represents the full response for one application request.

Fields:

- `TenantScope`: Effective tenant used by the request.
- `AppliedAt`: Timestamp used for lifecycle marker writes.
- `Candidates`: Per-candidate application results in input order.
- `AppliedCount`: Number of candidates that applied a new marker.
- `AlreadySatisfiedCount`: Number of candidates that were already satisfied by existing marker state.
- `SkippedCount`: Number of candidates skipped by deterministic preflight rules.
- `FailedCount`: Number of candidates rejected with diagnostics.

Validation rules:

- Every input candidate must produce exactly one candidate result.
- Counts must match per-candidate statuses.
- The result must not include resources from another tenant.

Relationships:

- Contains candidate application results.
- Contains diagnostics through per-candidate results.

## Candidate Application Result

Represents the outcome for one input candidate.

Fields:

- `Index`: Zero-based input candidate index.
- `Status`: Applied, already satisfied, skipped, or failed.
- `PolicyId`: Candidate policy identifier when available.
- `Outcome`: Candidate outcome when available.
- `ResourceId`: Candidate resource identifier when available.
- `ResourceVersion`: Candidate version when available.
- `Marker`: Effective lifecycle marker when application succeeded or was already satisfied.
- `Diagnostics`: Stable diagnostics for skipped or failed candidates.

Validation rules:

- Applied status means a new lifecycle marker was written.
- Already satisfied status means the requested marker state already existed.
- Skipped status is reserved for deterministic request-level non-write outcomes such as duplicate same-outcome candidates when the primary candidate already represents the requested work.
- Failed status means the candidate did not apply and includes at least one diagnostic.
- Conflicting lifecycle outcomes for the same resource in one request all fail for that resource before either marker is applied.

Relationships:

- Belongs to one policy application result.
- May reference one effective lifecycle marker.

## Application Diagnostic

Represents stable per-candidate failure or skip information.

Fields:

- `Code`: Stable diagnostic code.
- `Message`: Human-readable explanation.
- `Path`: Candidate field path when applicable.
- `PolicyId`: Policy involved when available.
- `ResourceId`: Resource involved when available.
- `ResourceVersion`: Resource version involved when available.

Candidate codes:

- `policy-application-candidate-invalid`
- `policy-application-outcome-unsupported`
- `policy-pruning-preview-only`
- `policy-application-stale-candidate`
- `policy-application-policy-missing`
- `policy-application-policy-mismatch`
- `policy-application-conflicting-outcome`
- `lifecycle-marker-target-not-found`
- `lifecycle-marker-conflict`

Validation rules:

- Diagnostics must be stable enough for hosts and tests to distinguish failure categories.
- Diagnostics must not leak resources outside the effective tenant.
- Resources outside the effective tenant surface as `lifecycle-marker-target-not-found`; application must not perform cross-tenant existence checks solely to diagnose tenant mismatch.

## Lifecycle Marker Outcome

Represents an application-supported write-side outcome.

Values:

- Archive maps to archived lifecycle marker state.
- Soft-delete maps to soft-deleted lifecycle marker state.

Validation rules:

- Archive and soft-delete reuse existing lifecycle marker idempotency and conflict behavior.
- None, retain, prune-preview, and unknown outcomes are not marker write outcomes.

## State Transitions

```text
Host evaluates policies
  -> receives preview candidates
  -> selects archive/soft-delete candidates
  -> submits policy application request
  -> resolve effective tenant
  -> validate request and candidate shape
  -> preflight same-resource conflicting lifecycle outcomes
  -> read latest target resources in tenant
  -> validate stale resource versions
  -> validate current policy declaration exists and outcome matches
  -> delegate supported marker writes to lifecycle marker service
  -> return one result per input candidate
```

Prohibited transitions:

- Preview directly applying markers.
- Application deleting resource versions.
- Application deactivating active versions.
- Application changing policy declarations.
- Application invoking new lifecycle hook behavior.
- Application crossing tenant boundaries.
