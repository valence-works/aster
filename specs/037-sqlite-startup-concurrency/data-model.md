# Data Model: SQLite Startup Concurrency Hardening

## Concurrent Startup Attempt

Represents one provider/service construction path participating in a shared startup window.

Fields:

- `AttemptIndex`: Deterministic test index used to create several startup attempts.
- `DatabasePath`: Shared temporary SQLite database path.
- `InitializeSchema`: Existing provider option controlling whether schema initialization is active.
- `Tenant`: Existing tenant scope used by any follow-up reads/writes.

Validation rules:

- Attempts in a scenario share the same database path.
- Attempts do not introduce new product-level state.
- The test observes only completion and final database state.

## Fresh Startup Database

Represents a SQLite database path with no existing file before concurrent initialization begins.

Fields:

- `DatabasePath`: Temporary file path.
- `InitialFileExists`: Must be false before startup.
- `PostStartupSchema`: Expected provider-created SQLite JSON schema.

Validation rules:

- Startup attempts must complete without corrupting schema creation.
- The database must remain usable through existing resource definition/resource APIs after startup.

## Existing Startup Database

Represents a tenant-aware SQLite JSON database containing persisted state before concurrent startup.

Fields:

- `DefinitionId`: Seeded resource definition identifier.
- `ResourceId`: Seeded resource identifier.
- `ResourceVersion`: Seeded resource version.
- `ActivationChannel`: Seeded activation channel.
- `LifecycleMarkerState`: Seeded lifecycle marker state.
- `Tenant`: Seeded tenant scope.

Validation rules:

- Concurrent startup must preserve seeded definitions, resources, activation state, and lifecycle markers.
- Tenant-aware table metadata must remain present after startup.
- Startup must not create duplicate persisted rows or bootstrap-only leftover tables.

## Passive No-Schema Construction

Represents concurrent service construction with automatic schema initialization disabled.

Fields:

- `DatabasePath`: Temporary file path.
- `InitializeSchema`: Always false.
- `ConstructedServices`: Identity, capability, or provider services that should not force schema initialization.

Validation rules:

- Concurrent construction must not create the SQLite file.
- No implicit schema initialization side effect is allowed.
