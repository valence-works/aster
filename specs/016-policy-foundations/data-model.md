# Data Model: Policy Foundations

## Policy Declaration

Represents declarative policy intent attached to a resource definition version.

Fields:

- `PolicyId`: Stable identifier unique within the definition version.
- `Name`: Host-visible policy name.
- `Kind`: Retention, archive, soft-delete, or version pruning.
- `Target`: Resource or resource version target.
- `Outcome`: Retain, archive, soft-delete, or prune-preview.
- `Criteria`: Explicit policy criteria.
- `Settings`: Kind-specific settings such as age threshold or retained-version count.

Validation rules:

- Policies attach to resource definitions only.
- `PolicyId`, `Kind`, `Target`, `Outcome`, and criteria must be present.
- The policy kind, target, and outcome must be compatible.
- Criteria are limited to age thresholds, retained-version counts, activation state, lifecycle marker state, definition identity, and tenant boundary.
- Arbitrary facet predicates, expression trees, SQL fragments, provider-specific query syntax, and callbacks are invalid.
- Version pruning declarations are allowed, but pruning writes are not.

Relationships:

- Stored on `ResourceDefinition`.
- Evaluated against resources of the declaring definition inside one effective tenant.
- Preserved by portability when the containing definition is exported.

## Policy Criteria

Represents the explicit matching criteria supported by this slice.

Fields:

- `MinimumAge`: Optional age threshold relative to a host-supplied evaluation timestamp.
- `MaximumRetainedVersions`: Optional count used by pruning previews.
- `ActivationState`: Optional active/draft state criterion.
- `LifecycleState`: Optional none, archived, or soft-deleted criterion.
- `DefinitionId`: Effective definition identity, derived from the declaring definition.
- `TenantScope`: Effective tenant boundary, supplied by the evaluation request.

Validation rules:

- Age-based evaluation requires an explicit `EvaluationTimestamp`.
- Retained-version count must be greater than zero when supplied.
- Lifecycle state must be one of the SDK-defined marker states.
- Criteria must not select resources outside the effective tenant.

Relationships:

- Belongs to one policy declaration.
- Produces zero or more policy candidate outcomes during preview.

## Policy Evaluation Request

Represents a host-requested policy preview.

Fields:

- `TenantScope`: Optional tenant scope; omitted means the default single-tenant scope.
- `DefinitionIds`: Optional bounded definition set to evaluate.
- `PolicyIds`: Optional bounded policy set to evaluate.
- `EvaluationTimestamp`: Required timestamp for age-based criteria.

Validation rules:

- The request must resolve to exactly one effective tenant.
- Age-based policy evaluation must fail closed when the timestamp is missing.
- The request must not imply cross-tenant evaluation.
- Policy evaluation must not mutate resources, versions, activation state, lifecycle markers, definitions, or snapshots.

Relationships:

- Reads policy declarations from definition storage.
- Reads candidate resource versions and marker state from provider-backed stores.
- Produces a policy evaluation preview.

## Policy Evaluation Preview

Represents deterministic preview output for a policy evaluation request.

Fields:

- `TenantScope`: Effective tenant evaluated.
- `EvaluationTimestamp`: Timestamp used for age calculations.
- `Candidates`: Candidate outcomes.
- `Diagnostics`: Validation or preview diagnostics.

Validation rules:

- Preview generation must not write data.
- Candidate outcomes must include the matching policy declaration and affected resource ID.
- Version pruning candidates must include affected version IDs or version numbers.
- Unsafe pruning previews that would remove all retained versions must produce diagnostics.

Relationships:

- Contains zero or more policy candidate outcomes.
- Contains zero or more policy diagnostics.

## Policy Candidate Outcome

Represents one resource or version that matched a policy declaration during preview.

Fields:

- `PolicyId`: Matching declaration.
- `PolicyKind`: Matching policy kind.
- `Outcome`: Archive, soft-delete, retain, or prune-preview.
- `ResourceId`: Affected resource.
- `ResourceVersion`: Affected version when applicable.
- `Reason`: Host-visible explanation.

Validation rules:

- Archive and soft-delete candidates are informational until the host explicitly applies marker writes.
- Pruning candidates are preview-only and must not be applied by this slice.
- Candidate resources and versions must belong to the effective tenant.

## Lifecycle Marker State

Represents the current lifecycle marker for one resource.

Fields:

- `TenantScope`: Effective tenant boundary.
- `ResourceId`: Logical resource identifier.
- `State`: None, archived, or soft-deleted.
- `MarkedAt`: Timestamp supplied by the host operation.
- `Reason`: Optional host-visible reason.

Validation rules:

- `(TenantScope, ResourceId)` has at most one effective marker state.
- Applying the same non-none state is idempotent.
- Applying archived to a soft-deleted resource, or soft-deleted to an archived resource, fails with a stable diagnostic.
- Marker writes must not prune versions, physically delete resources, deactivate active versions, or rewrite resource history.
- Restore and marker transition workflows are out of scope.

Relationships:

- References one logical resource inside one tenant.
- Queried explicitly through lifecycle-state criteria.
- Preserved by portability when the referenced resource is exported.

## Lifecycle Marker Write Request

Represents an explicit host operation to apply archive or soft-delete state.

Fields:

- `TenantScope`: Optional tenant scope; omitted means the default single-tenant scope.
- `ResourceId`: Target logical resource.
- `State`: Archived or soft-deleted.
- `MarkedAt`: Host-supplied timestamp.
- `Reason`: Optional host-visible reason.

Validation rules:

- Target resource must exist in the effective tenant.
- State must be archived or soft-deleted.
- Repeated same-state writes are idempotent.
- Conflicting writes fail closed without changing existing marker state.

## Resource Query Lifecycle Criterion

Represents explicit lifecycle-state filtering in the portable resource query model.

Fields:

- `LifecycleState`: Optional none, archived, or soft-deleted criterion.

Validation rules:

- Omitted lifecycle-state criterion must not hide or exclude resources by marker state.
- When supplied, filtering applies inside the effective tenant.
- Providers must fail closed if they cannot support lifecycle-state filtering.

Relationships:

- Uses lifecycle marker state separate from immutable resource versions.
- May combine with existing definition, version scope, activation, filter, sort, skip, and take behavior.

## Policy Diagnostic

Represents stable validation or preview failure output.

Fields:

- `Code`: Stable diagnostic code.
- `Message`: Human-readable explanation.
- `PolicyId`: Policy involved when available.
- `ResourceId`: Resource involved when available.
- `ResourceVersion`: Version involved when available.

Candidate codes:

- `policy-invalid`
- `policy-kind-unsupported`
- `policy-outcome-unsupported`
- `policy-target-invalid`
- `policy-criteria-unsupported`
- `policy-conflict`
- `policy-evaluation-timestamp-required`
- `policy-pruning-unsafe`
- `lifecycle-marker-conflict`
- `lifecycle-marker-target-not-found`

Reserved for future write-path enforcement:

- `policy-pruning-preview-only`

Validation rules:

- Diagnostics must be stable enough for tests and hosts to distinguish failure categories.
- Failures must not leak resources from outside the effective tenant.

## State Transitions

```text
Register definition with policy declarations
  -> Validate declaration shape
  -> Append new immutable definition version

Preview policies
  -> Resolve effective tenant
  -> Validate declarations and request timestamp
  -> Read bounded resources and marker state
  -> Return candidate outcomes and diagnostics without writes

Apply archive or soft-delete marker
  -> Resolve effective tenant
  -> Verify target resource exists
  -> If same marker exists: return current marker state
  -> If different marker exists: return conflict diagnostic
  -> Write additive marker state without changing resource versions or activation

Query by lifecycle state
  -> Resolve effective tenant
  -> Read candidate resources
  -> Apply explicit lifecycle-state criterion when supplied
```
