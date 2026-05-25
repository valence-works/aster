# Research: Policy Foundations

## Decision: Definition-Attached Policy Declarations

**Decision**: Policy declarations attach to `ResourceDefinition` versions and apply to resources of the declaring definition.

**Rationale**: Definitions are already the SDK boundary for resource type metadata. Attaching policies there keeps declarations discoverable, versioned with definition metadata, and visible before evaluation without creating per-resource override semantics.

**Alternatives considered**:

- Per-resource policy declarations: rejected because it creates override and precedence rules before there is a demonstrated need.
- Definition and resource declarations with overrides: rejected because it requires conflict resolution semantics that are outside this first slice.
- Runtime discovery or scanning: rejected because it hides behavior and violates explicitness requirements.

## Decision: Explicit Criteria Set Only

**Decision**: This slice supports only age thresholds, retained-version counts, activation state, lifecycle marker state, resource definition identity, and effective tenant boundary as policy criteria.

**Rationale**: These criteria cover retention, archive, soft-delete, and pruning previews without requiring a query planner or policy expression language. They are also portable across the in-memory and SQLite JSON providers.

**Alternatives considered**:

- Arbitrary resource facet predicates: rejected for this slice because they would overlap with query planning and require provider capability negotiation.
- Expression trees or callbacks: rejected because they are not portable and are hard to persist, validate, or execute provider-side.
- Raw SQL or provider-specific syntax: rejected by the public query model and provider-agnostic principles.

## Decision: Host-Supplied Evaluation Timestamp

**Decision**: Age-based policy previews require a host-supplied evaluation timestamp and must not read an ambient system clock.

**Rationale**: Retention and pruning previews must be deterministic in tests, repeatable in audits, and explicit in host workflows. A required timestamp also avoids hidden timezone and clock-source assumptions.

**Alternatives considered**:

- Use the system clock by default: rejected because previews would change based on when tests or hosts execute.
- Optional timestamp override: rejected because it still leaves ambient behavior as the default.
- Store but do not evaluate age criteria: rejected because preview behavior is a core requirement for this slice.

## Decision: Lifecycle Markers Are Additive State

**Decision**: Archive and soft-delete outcomes are stored as explicit lifecycle marker state separate from immutable resource version snapshots.

**Rationale**: Marker state must be writable without rewriting historical resource versions. A separate marker record follows the existing activation-state pattern: resource history remains append-only while current lifecycle state remains queryable and portable.

**Alternatives considered**:

- Add lifecycle state to `Resource` snapshots: rejected because applying a marker would require rewriting snapshots or creating a misleading resource version.
- Hide markers as query defaults: rejected because hosts must opt into lifecycle-state filtering explicitly.
- Physically delete or deactivate resources on soft-delete/archive: rejected because it conflicts with append-only history and channel activation separation.

## Decision: One Effective Lifecycle State

**Decision**: A resource has at most one effective lifecycle marker state in this slice. Reapplying the same marker is idempotent. Applying archive to a soft-deleted resource, or soft-delete to an archived resource, fails with a stable diagnostic.

**Rationale**: A single effective state keeps query behavior and portability clear while restore and lifecycle transition semantics remain undefined.

**Alternatives considered**:

- Allow archive and soft-delete to coexist: rejected because it complicates query semantics and future restore behavior.
- Latest marker wins: rejected because it creates implicit transitions without an explicit transition model.
- Always append marker history: rejected for this slice because audit history for marker transitions requires additional requirements.

## Decision: Pruning Preview Only

**Decision**: Version pruning can be declared, validated, and previewed, but destructive pruning writes are out of scope.

**Rationale**: Pruning can permanently remove history. Preview-only behavior lets hosts inspect candidate versions and diagnostics without compromising immutable-versioning guarantees in the first policy slice.

**Alternatives considered**:

- Implement pruning writes now: rejected because it needs stronger audit, backup, lifecycle hook, and restore semantics.
- Store pruning declarations but skip preview: rejected because previews are required to make pruning intent actionable and testable.

## Decision: Explicit Query Model Extension

**Decision**: Lifecycle-state queries use an explicit SDK query criterion rather than hidden filtering, raw SQL, or public `IQueryable<Resource>`.

**Rationale**: Hosts need to find archived and soft-deleted resources intentionally. Adding a small lifecycle-state criterion preserves the portable query model and lets providers translate behavior locally.

**Alternatives considered**:

- Implicitly hide soft-deleted resources: rejected because it creates surprising behavior and authorization-like filtering.
- Raw provider queries: rejected because public SQL and provider-specific syntax are out of scope.
- Reusing facet predicates: rejected because lifecycle state is SDK metadata, not resource payload.

## Decision: Small Core Services Over Policy Engine

**Decision**: Add focused core contracts for validation, preview evaluation, and lifecycle marker state rather than a provider registry, planner, scheduler, or policy engine.

**Rationale**: The current need is explicit declaration, deterministic preview, and marker writes. A small service surface is sufficient and remains easy to delete or replace.

**Alternatives considered**:

- General policy engine: rejected as premature infrastructure.
- Background retention scheduler: rejected because execution timing and hosting are host responsibilities.
- Provider registry or runtime scanning: rejected because Aster already uses explicit active provider registration.

## Decision: Provider-Local Storage Support

**Decision**: In-memory and SQLite JSON providers implement lifecycle marker storage and lifecycle-state query filtering locally. Resource definitions persist policy declaration metadata through existing definition storage.

**Rationale**: Policy semantics are core SDK behavior, but storage details belong to providers. This follows existing provider boundaries without adding dependencies or migration infrastructure.

**Alternatives considered**:

- Core-only in-memory marker store for all providers: rejected because SQLite-backed hosts need markers to persist with SQLite data.
- New storage/provider framework: rejected because existing provider abstractions are sufficient for this slice.
- Migration framework: rejected because this feature can use idempotent provider initialization or additive schema changes without defining a general migration policy.
