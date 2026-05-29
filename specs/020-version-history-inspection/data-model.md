# Data Model: Resource Version History Inspection

## Version History Request

Host-provided request for one resource timeline.

Fields:

- `TenantScope? TenantScope`: Optional tenant scope. Omitted means the default single-tenant scope.
- `string? ResourceId`: Logical resource identifier whose versions should be inspected.

Validation:

- `ResourceId` is required and must not be null, empty, or whitespace.
- Exactly one effective tenant is resolved for the request.

## Version History Result

Read-only response for one resource timeline.

Fields:

- `TenantScope TenantScope`: Effective tenant used for all reads.
- `string ResourceId`: Requested logical resource identifier.
- `IReadOnlyList<ResourceVersionSummary> Versions`: Ordered summaries for each version found in the effective tenant.

Rules:

- Missing resources return an empty `Versions` collection.
- Results are ordered by resource version number ascending.
- The result must not expose state from any other tenant.

## Resource Version Summary

Read-only description of one resource version.

Fields:

- `string ResourceVersionId`: Version snapshot identifier.
- `int Version`: Resource version number.
- `string DefinitionId`: Logical resource definition identifier.
- `int? DefinitionVersion`: Resource definition version captured by the resource version.
- `DateTime Created`: Version creation timestamp.
- `bool IsLatest`: Whether this is the highest version currently present for the resource.
- `bool IsDraft`: Whether this version is absent from all active channel state.
- `IReadOnlyList<string> ActiveChannels`: Active channel names containing this version.
- `ResourceLifecycleMarkerState LifecycleState`: Current lifecycle marker state for the logical resource, or `None` when no marker exists.
- `bool IsProtectedFromPruning`: Whether destructive pruning must protect this version because it is latest or active.
- `ResourceVersionMaintenanceDisposition MaintenanceDisposition`: Conservative maintenance hint for host displays.

Rules:

- `IsLatest` is true only for the highest returned version number.
- `IsDraft` is true when `ActiveChannels` is empty.
- `ActiveChannels` are deterministic and sorted ordinally.
- `LifecycleState` is resource-level current state, not a per-version marker.
- `IsProtectedFromPruning` is true when `IsLatest` is true or `ActiveChannels` is non-empty.

## Maintenance Disposition

Conservative host-facing signal describing obvious maintenance safety.

Values:

- `Protected`: Version is latest or active and must not be destructively pruned.
- `PossibleCandidate`: Version is historical, inactive, and not latest; policy evaluation is still required before any pruning.

Rules:

- The disposition does not apply policy retention counts.
- The disposition does not authorize mutation.
- The disposition does not replace policy preview/application diagnostics.

## Activation State Read

Provider-facing read model for active channels.

Input:

- Effective tenant.
- One or more logical resource identifiers.

Output:

- Activation states for those resource identifiers in the effective tenant.

Rules:

- Empty, null, or whitespace resource identifiers are ignored.
- Reads must not return activation states outside the effective tenant.
- Providers may return no states for resources that do not exist or have no active channels.
