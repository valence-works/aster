# Contract: Policy Foundations

This contract describes public SDK behavior for policy declarations, previews, lifecycle markers, and lifecycle-state querying. Names are provisional for planning, but behavior is normative.

## Policy Declaration Behavior

The SDK MUST support explicit policy declarations attached to resource definitions.

Required behavior:

- Policy declarations MUST be stored on resource definition versions.
- Policy declarations MUST NOT attach directly to individual resources in this slice.
- Registering a definition with policy declarations MUST append a normal immutable definition version.
- Existing definitions without policy declarations MUST remain valid.
- Policy declarations MUST be inspectable before validation or evaluation.
- Policy declarations MUST NOT execute by themselves.

Supported policy intent:

- Retention intent.
- Archive intent.
- Soft-delete intent.
- Version pruning intent.

Supported criteria:

- Age thresholds.
- Retained-version counts.
- Activation state.
- Lifecycle marker state.
- Resource definition identity.
- Effective tenant boundary.

Unsupported criteria:

- Arbitrary resource facet predicates.
- Expression trees.
- SQL fragments.
- Provider-specific query syntax.
- Host code callbacks.

## Policy Validation Behavior

The SDK MUST expose validation behavior for policy declarations.

Required behavior:

- Validation MUST report stable diagnostics for missing, invalid, contradictory, or unsupported metadata.
- Validation MUST NOT mutate resources, resource versions, activation state, lifecycle markers, definitions, or portable snapshots.
- Validation MUST fail closed for unsupported criteria or outcomes.
- Validation MUST distinguish unsupported policy kind, unsupported outcome, invalid target, unsupported criteria, conflicting declarations, and preview-only pruning enforcement.

## Policy Preview Behavior

The SDK MUST expose a host-requested policy preview workflow.

Required behavior:

- Preview requests MUST resolve one effective tenant.
- Preview requests MUST be bounded by explicit host input such as tenant and optional definition/policy selection.
- Age-based previews MUST require a host-supplied evaluation timestamp.
- Preview services MUST NOT read an ambient system clock.
- Preview results MUST include candidate outcomes, matching policy declaration identifiers, affected resource identifiers, affected version identifiers when applicable, and diagnostics.
- Preview generation MUST NOT archive, soft-delete, prune, deactivate, delete, or otherwise mutate resource data.
- Version pruning MUST remain preview-only in this slice.
- Unsafe pruning previews that would remove all retained versions of a resource MUST produce diagnostics.

Out of scope:

- Automatic policy execution.
- Background retention jobs.
- Schedulers.
- Destructive pruning writes.
- Restore workflows.

## Lifecycle Marker Behavior

The SDK MUST represent archive and soft-delete outcomes as explicit lifecycle marker state.

Required behavior:

- Hosts MUST be able to explicitly apply archive and soft-delete markers to resources.
- Marker writes MUST be host-requested and MUST NOT occur as a side effect of preview.
- Marker writes MUST NOT physically delete resources or resource versions.
- Marker writes MUST NOT deactivate active versions.
- Marker writes MUST NOT rewrite historical resource data.
- Each resource MUST have at most one effective lifecycle marker state.
- Reapplying the same marker MUST be idempotent.
- Applying archive to a soft-deleted resource, or soft-delete to an archived resource, MUST fail with a stable diagnostic and leave existing marker state unchanged.
- Marker state MUST be scoped by effective tenant.

Out of scope:

- Marker restore.
- Marker transition workflows.
- Marker history/audit event stream beyond the effective marker state.

## Query Behavior

The portable query model MUST support explicit lifecycle-state criteria.

Required behavior:

- Hosts MUST be able to query resources by lifecycle marker state through explicit criteria.
- Omitted lifecycle-state criteria MUST NOT hide archived or soft-deleted resources.
- Lifecycle-state filtering MUST apply inside the effective tenant boundary.
- Existing definition, version scope, activation, filter, sort, skip, and take behavior MUST remain deterministic.
- Providers MUST fail closed if lifecycle-state filtering is requested but unsupported.

Prohibited behavior:

- Public raw SQL.
- Public `IQueryable<Resource>`.
- Hidden default exclusion of soft-deleted or archived resources.

## Portability Behavior

Portable workflows MUST preserve policy declarations and lifecycle markers when the selected scope includes them.

Required behavior:

- Exported definitions MUST include policy declarations stored on those definition versions.
- Exported resources MUST include lifecycle marker state when the referenced resource is included in scope.
- Import preview MUST report policy and marker conflicts with stable diagnostics.
- Write import MUST preserve lifecycle marker state inside the target tenant without rewriting resource versions.
- Snapshots MUST remain single-source-tenant and imports must target one tenant.

## Provider Behavior

In-memory and SQLite JSON providers MUST implement the storage and query behavior needed by this feature.

Required behavior:

- In-memory storage MUST persist policy declarations with definitions and lifecycle markers with resource state.
- SQLite JSON storage MUST persist policy declarations with definitions and lifecycle markers in provider-owned storage.
- SQLite initialization MAY add idempotent additive storage support for lifecycle markers, but MUST NOT introduce a general migration framework.
- Providers MUST keep tenant filtering before policy preview and lifecycle query results can cross tenant boundaries.

## Compatibility

The feature MUST remain additive for existing hosts.

Required behavior:

- Existing `AddAsterCore()` and `AddAsterSqliteJson()` registration behavior MUST remain compatible.
- Existing callers that do not declare policies MUST observe no automatic policy behavior.
- Existing reads, writes, activation behavior, schema upgrades, portability, lifecycle hooks, and query behavior MUST continue to work without policy declarations.
- Existing default single-tenant behavior MUST remain valid.

## Out Of Scope

This feature MUST NOT introduce:

- background schedulers;
- hidden retention jobs;
- automatic policy execution;
- authorization or permission policy engines;
- cross-tenant policy evaluation;
- runtime scanning;
- provider registries;
- arbitrary facet predicate policy criteria;
- provider-specific policy languages;
- public raw SQL;
- public `IQueryable<Resource>`;
- destructive pruning writes;
- restore workflows.
