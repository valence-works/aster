<!--
SYNC IMPACT REPORT
Version Change: 0.0.0 -> 1.0.0
Modified Principles:
- N/A (Initial Ratification)
Added Sections:
- Core Principles (SDK-First, Immutable Versioning, Channel-Based Activation, Typed & Queryable, Provider Agnostic)
- Governance (Coding Standards, Semantic Versioning)
Templates Pending Update:
- None
-->
# Aster Constitution

## Core Principles

### I. SDK-First & Headless
**Aster provides the building blocks (definitions, storage, querying) but enforces no specific UI or host environment.**
- It MUST implement implementation-agnostic contracts.
- It MUST NOT depend on a specific CMS or UI framework (e.g., Orchard Core UI).
- Scenarios enabled: Headless CMS, Workflow Engine, Configuration Service.

### II. Immutable Versioning
**Resource instances are append-only. State mutations create new versions.**
- History MUST be preserved by default to enable audit and time-travel.
- Concurrency MUST be handled via optimistic locking on version identity.
- "Update" operations always result in a new `ResourceVersion` entry.

### III. Channel-Based Activation
**"Published" is just one state. Resources can be active in multiple channels simultaneously.**
- The system MUST support parallel activation contexts (e.g., Preview, Mobile, A/B Testing).
- Activation MUST be decoupled from the resource payload itself.
- A resource can be "Draft" and "Published" and "Archived" concurrently in different channels if required by the host.

### IV. Typed & Queryable
**The system supports strongly-typed Aspects (POCOs) and a portable Query Model.**
- Developers MUST be able to define Aspects as C# classes/records.
- Queries MUST be defined using a portable object model (AST), not raw SQL or generic `IQueryable` leakage.
- Indexing MUST support "Safe Evaluation" to prevent query injection or platform-specific lock-in.

### V. Provider Agnostic
**No hard dependencies on specific databases (like YesSQL or Orchard Core).**
- Business logic MUST NOT depend on EF Core or specific DB providers directly.
- Persistence is pluggable via defined abstractions (`IResourceWriteStore`, `IResourceQueryService`).
- Infrastructure steps (migrations) MUST be abstract to support both SQL and Document stores.

## Governance

### Coding Standards
- Development MUST adhere to `docs/coding-conventions.md`.
- Public APIs MUST use `CancellationToken` and `Async` patterns.
- Data structures for storage MUST be serializable and version-tolerant.
- Nullability MUST be enabled and strict; avoid `null` unless semantically valid.

### Versioning
- The project follows **Semantic Versioning 2.0.0**.
- **MAJOR**: Breaking changes to the core SDK contracts or storage format.
- **MINOR**: New features, aspects, or provider implementations.
- **PATCH**: Bug fixes and performance improvements.

### Architecture Reviews
- Major phase transitions (as defined in `docs/roadmap.md`) REQUIRE an architecture review document.
- New dependencies MUST be justified against the "Provider Agnostic" principle.

**Version**: 1.0.0 | **Ratified**: 2026-03-04 | **Last Amended**: 2026-03-04
