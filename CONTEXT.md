# Aster

Aster is a headless SDK for resource definitions, versioned resources, activation state, lifecycle markers, policy workflows, portability, and provider-backed querying.

## Language

**Resource**:
A versioned domain record stored under a resource definition. A resource can have operational state beside its immutable versions.
_Avoid_: Entity, item, record

**Lifecycle Marker**:
Operational state that marks a resource as archived or soft-deleted without rewriting the resource version.
_Avoid_: Resource status, lifecycle flag, deletion flag

**Lifecycle Marker Transition**:
A requested change to a resource's lifecycle marker, such as applying an archive or soft-delete marker, or clearing an expected marker during restore.
_Avoid_: Marker update, status change, delete operation
