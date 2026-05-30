# Data Model: Batch Version History Inspection

## Batch Version History Request

Represents a host request to inspect histories for a selected set of resources.

Fields:

- `TenantScope`: Optional tenant scope. When omitted, the default single-tenant scope is used.
- `ResourceIds`: Explicit logical resource identifiers selected by the caller.

Validation:

- Request object must not be null.
- `ResourceIds` must not be null.
- Empty `ResourceIds` is valid and returns no histories.
- Each resource identifier must be non-null, non-empty, and non-whitespace.
- Duplicate identifiers are collapsed with ordinal comparison, preserving first-seen order.

## Batch Version History Result

Represents the result for one batch request.

Fields:

- `TenantScope`: Effective tenant used for all reads.
- `Histories`: Ordered list of per-resource history results, one for each distinct requested identifier.

Rules:

- Histories follow the distinct resource identifier order from the request.
- Histories for missing resources are included with empty version lists.
- All histories use the same effective tenant.

## Resource Version History

Existing per-resource read-only history.

Fields:

- `TenantScope`: Effective tenant.
- `ResourceId`: Logical resource identifier.
- `Versions`: Ordered version summaries.

Rules:

- Version summaries are ordered by version number ascending.
- Missing resources return an empty `Versions` list.
- Semantics must match existing single-resource history inspection.

## Resource Version Summary

Existing read-only version summary.

Fields:

- Resource version identity and version number.
- Definition identity and optional definition version.
- Creation timestamp.
- Latest flag.
- Draft flag.
- Ordered active channel names.
- Current resource lifecycle marker state.
- Pruning protection flag.
- Maintenance disposition.

Rules:

- Latest version is protected.
- Versions active in any channel are protected.
- Historical inactive non-latest versions are possible maintenance candidates.
- Lifecycle marker state is resource-level current state applied to each version summary.

## State Transitions

None. Batch version history inspection is read-only and must not create, update, delete, activate, deactivate, archive, restore, prune, persist summaries, or mutate lifecycle markers.
