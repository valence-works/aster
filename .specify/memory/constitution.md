<!--
SYNC IMPACT REPORT
Version Change: 1.1.0 -> 1.1.1
Modified Principles:
- 16. Simplicity First (added rationale)
- 17. Modern C# Idioms (added rationale)
- 18. Readability Over Cleverness (added rationale)
- 19. Explicitness Over Magic (added rationale)
- 20. Abstractions Must Earn Their Existence (added rationale)
- 21. Optimize For Deletion (added rationale)
- 22. Favor Composition Over Inheritance (added rationale)
- 23. Dependencies Should Remain Intentional (added rationale)
- 24. Operational Simplicity Matters (added rationale)
Added Sections:
- None
Removed Sections:
- None
Templates Requiring Updates:
- ✅ .specify/templates/plan-template.md (no changes required)
- ✅ .specify/templates/spec-template.md (no changes required)
- ✅ .specify/templates/tasks-template.md (no changes required)
Follow-up TODOs:
- None
-->
# Aster Constitution

## Core Principles

### I. SDK-First & Headless
**Aster provides the building blocks (definitions, storage, querying) but enforces no specific UI or host environment.**
- It MUST implement implementation-agnostic contracts.
- It MUST NOT depend on a specific CMS or UI framework (e.g., Orchard Core UI).
- It MUST remain suitable for headless CMS, workflow engine, configuration service, and similar host scenarios.

### II. Immutable Versioning
**Resource instances are append-only. State mutations create new version snapshots.**
- History MUST be preserved by default to enable audit and time-travel.
- Concurrency MUST be handled via optimistic locking on version identity.
- Update operations MUST create a new immutable `Resource` version snapshot rather than mutating an existing snapshot.

### III. Channel-Based Activation
**"Published" is just one state. Resources can be active in multiple channels simultaneously.**
- The system MUST support parallel activation contexts (e.g., Preview, Mobile, A/B Testing).
- Activation MUST be decoupled from the resource payload itself.
- A resource can be draft in one context and active in one or more host-defined channels.

### IV. Typed & Queryable
**The system supports strongly typed aspects (POCOs) and a portable query model.**
- Developers MUST be able to define aspects as C# classes or records.
- Queries MUST be defined using a portable object model (AST), not raw SQL or public `IQueryable` leakage.
- Query execution MUST avoid injection-prone string composition and provider lock-in.

### V. Provider Agnostic
**Core business logic does not depend on a specific database or provider framework.**
- Business logic MUST NOT depend on EF Core, YesSQL, MongoDB, SQLite, PostgreSQL, or any specific provider directly.
- Persistence MUST be pluggable via defined abstractions such as `IResourceVersionReader`, `IResourceVersionWriter`, `IResourceDefinitionStore`, and `IResourceQueryService`.
- Provider infrastructure steps MUST support both SQL-style migrations and document-store provisioning without forcing one model onto all providers.

## Engineering Principles

### 16. Simplicity First
**Solutions SHOULD prefer the simplest architecture and implementation that correctly satisfies current requirements.**
- Avoid speculative abstractions, premature generalization, and unnecessary infrastructure.
- A simpler direct implementation SHOULD be preferred until a real use case proves it insufficient.
- Rationale: Simpler designs reduce review burden, make provider behavior easier to reason about, and leave room to evolve once requirements are proven.

### 17. Modern C# Idioms
**Code SHOULD use modern C# and .NET features when they improve clarity, correctness, and conciseness.**
- Appropriate idioms include records, primary constructors, collection expressions, pattern matching, async streams, minimal APIs, and nullable reference types.
- Modern language features SHOULD NOT be used when they make behavior harder to understand for experienced .NET developers.
- Rationale: Modern C# can express immutable data, async workflows, and domain intent clearly, but language features are tools rather than goals.

### 18. Readability Over Cleverness
**Code MUST optimize for maintainability and clarity by experienced .NET developers.**
- Cleverness, excessive indirection, unnecessary metaprogramming, and obscure control flow SHOULD be avoided.
- Names, boundaries, and behavior SHOULD make the code easy to review and change.
- Rationale: Aster is SDK infrastructure; maintainers and consumers need predictable code paths more than compact tricks.

### 19. Explicitness Over Magic
**Behavior SHOULD be discoverable through code and configuration rather than hidden conventions.**
- Runtime scanning, implicit side effects, and convention-only behavior SHOULD be avoided unless they are clearly documented and justified.
- Configuration and registration paths SHOULD make provider choices and operational behavior explicit.
- Rationale: Explicit behavior makes provider selection, persistence semantics, and query support visible during development and debugging.

### 20. Abstractions Must Earn Their Existence
**Interfaces, layers, generic pipelines, and extension points SHOULD only be introduced when they solve a demonstrated need.**
- Avoid designing hypothetical frameworks inside the product.
- New abstractions SHOULD identify the current duplication, provider boundary, test seam, or lifecycle need they address.
- Rationale: Every abstraction becomes part of the SDK's maintenance and learning surface, so it must pay for itself in present product value.

### 21. Optimize For Deletion
**The architecture SHOULD make it easy to remove, replace, or simplify components later.**
- Small composable modules are preferred over deeply intertwined systems.
- Features SHOULD avoid broad coupling and global side effects that make later deletion expensive.
- Rationale: Early-stage architecture changes often reveal simpler paths; replaceable components let the project adapt without large rewrites.

### 22. Favor Composition Over Inheritance
**Composition, explicit contracts, and data-oriented design SHOULD generally be preferred over deep inheritance hierarchies.**
- Inheritance SHOULD be shallow and purposeful when used.
- Reusable behavior SHOULD usually be expressed through services, records, functions, and explicit collaborators.
- Rationale: Composition keeps provider behavior and lifecycle rules local, testable, and easier to recombine across host scenarios.

### 23. Dependencies Should Remain Intentional
**The solution SHOULD minimize unnecessary third-party dependencies.**
- Prefer platform capabilities before introducing additional frameworks.
- New dependencies MUST be justified by clear product value, operational value, or meaningful risk reduction.
- Rationale: Dependencies affect consumers, deployment, security review, versioning, and long-term portability.

### 24. Operational Simplicity Matters
**Deployment, debugging, local development, and observability SHOULD remain straightforward.**
- Operational complexity MUST be justified by clear product value.
- Provider setup, migrations/provisioning, logs, and local test workflows SHOULD be understandable without specialized infrastructure.
- Rationale: A headless SDK should be easy to embed and diagnose in many host applications, including small local and test environments.

## Governance

### Coding Standards
- Development MUST adhere to `docs/coding-conventions.md`.
- Public APIs MUST use `CancellationToken` and async patterns where operations can block or perform I/O.
- Data structures for storage MUST be serializable and version-tolerant.
- Nullability MUST be enabled and strict; avoid `null` unless semantically valid.

### Versioning
- The project follows **Semantic Versioning 2.0.0**.
- **MAJOR**: Breaking changes to the core SDK contracts, storage format, or governance principles.
- **MINOR**: New principles, new features, aspects, or provider implementations.
- **PATCH**: Bug fixes, performance improvements, typo fixes, and non-semantic clarifications.

### Architecture Reviews
- Major phase transitions (as defined in `docs/Roadmap.md`) REQUIRE an architecture review document.
- New dependencies MUST be justified against the "Provider Agnostic" and "Dependencies Should Remain Intentional" principles.
- New abstractions MUST be justified against the "Abstractions Must Earn Their Existence" principle.

### Spec Kit Compliance
- Every implementation plan MUST include a Constitution Check before research/design work proceeds.
- The Constitution Check MUST explicitly consider simplicity, abstraction justification, dependency justification, provider boundaries, operational impact, and testability.
- If a feature intentionally violates a SHOULD-level principle, the plan MUST explain why and identify the simpler alternative that was rejected.

**Version**: 1.1.1 | **Ratified**: 2026-03-04 | **Last Amended**: 2026-05-15
