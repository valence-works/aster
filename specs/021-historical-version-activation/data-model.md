# Data Model: Historical Version Activation

## Historical Version Activation

Existing activation operation targeting a non-latest but existing resource version.

Fields:

- Resource ID.
- Resource version number.
- Activation channel.
- Effective tenant scope.
- `allowMultipleActive` flag.

Rules:

- The resource version must exist in the effective tenant.
- Latest version identity is not changed.
- Resource version payload is not changed.
- Activation state is the only mutated state.

## Active Version Set

Channel-specific ordered list of active version numbers for a logical resource.

Rules:

- When `allowMultipleActive` is false, the resulting set contains only the requested version.
- When `allowMultipleActive` is true, the requested version is added to existing active versions.
- Resulting version numbers are ordered deterministically.
- Tenant scope bounds all reads and writes.

## Lifecycle Hook Context

Existing activation hook context describing the activation attempt.

Rules:

- `Version` is the requested version, even when historical.
- `ActiveVersions` contains the resulting active version set.
- Hook invocation order and rejection/failure behavior remain unchanged.
