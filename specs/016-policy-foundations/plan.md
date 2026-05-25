# Implementation Plan: Policy Foundations

**Branch**: `016-policy-foundations` | **Date**: 2026-05-25 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/016-policy-foundations/spec.md`

## Summary

Add explicit policy foundations for retention, archive, soft-delete, and version pruning without automatic execution. The implementation adds policy declaration metadata to resource definitions, validation and deterministic preview services, explicit archive/soft-delete lifecycle marker operations, lifecycle-state query criteria, portability support for policy metadata and markers, and provider implementations for in-memory and SQLite JSON. Version pruning remains preview-only. The slice does not introduce background jobs, hidden retention behavior, authorization policies, runtime scanning, provider registries, public SQL, public `IQueryable<Resource>`, restore workflows, arbitrary facet predicates, or provider-specific policy languages.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, in-memory store, SQLite JSON provider, resource manager/store abstractions, query capability/validation stack, portability service, lifecycle hook dispatcher, xUnit test stack; no new dependencies  
**Storage**: Existing resource definitions gain policy declaration metadata; resource lifecycle markers are stored as additive state separate from immutable resource versions; portable snapshots include policy declarations and lifecycle markers; SQLite JSON adds policy/marker storage without a general migration framework  
**Testing**: `dotnet test Aster.sln`, focused policy declaration/validation/preview/lifecycle marker/query/portability tests, SQLite JSON provider tests, existing single-tenant and tenant-scoped regression tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment  
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: Existing no-policy operations remain behaviorally equivalent and continue to pass existing regression tests; lifecycle-marker lookups occur only when marker state is explicitly written, queried, exported, imported, or used by policy preview; policy preview uses bounded host-supplied scope and avoids scanning outside the effective tenant in provider-backed execution  
**Constraints**: Policy declarations attach to resource definitions only; matching criteria are limited to age thresholds, retained-version counts, activation state, lifecycle marker state, definition identity, and tenant boundary; age-based previews require a host-supplied evaluation timestamp; archive/soft-delete marker writes are explicit host operations; same-marker writes are idempotent; conflicting marker writes fail closed; pruning writes and restore transitions are out of scope; no new dependencies, schedulers, hidden jobs, provider registry, runtime scanning, public SQL, or public `IQueryable<Resource>`  
**Scale/Scope**: Current core SDK plus in-memory and SQLite JSON provider support for policy declarations, validation, previews, lifecycle markers, lifecycle-state queries, portability, documentation, and tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK/library contracts and provider behavior only; no UI, CMS, host framework, authorization, or scheduler coupling.
- **Immutable versioning**: PASS - Resource versions remain append-only; lifecycle markers are additive state outside version snapshots.
- **Channel activation**: PASS - Activation state remains separate from resource payloads and is only used as explicit policy/query criteria.
- **Typed/queryable**: PASS - Querying remains through the portable query model; lifecycle-state criteria are explicit model fields, not public SQL or `IQueryable`.
- **Provider agnostic**: PASS - Core defines policy semantics and provider abstractions; in-memory and SQLite translate them locally.
- **Simplicity first**: PASS - The slice uses definition metadata, small services, and explicit store operations instead of a policy engine or registry.
- **Modern C# idioms**: PASS - Records, nullable-safe request models, collection expressions, and async APIs match existing SDK style.
- **Readability over cleverness**: PASS - Policy declaration, preview, marker write, and query behavior are direct and testable.
- **Explicitness over magic**: PASS - No ambient clock, runtime scanning, automatic execution, or hidden filtering.
- **Abstractions justified**: PASS - New policy and lifecycle marker contracts are needed because the behavior crosses core, providers, queries, and portability.
- **Optimize for deletion**: PASS - Policy models and marker stores are localized and can be removed without changing resource version payloads.
- **Composition over inheritance**: PASS - Uses composed records/services and avoids inheritance hierarchies.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - Existing build/test workflows remain sufficient; no worker process, migration runner, or external service.

## Project Structure

### Documentation (this feature)

```text
specs/016-policy-foundations/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- policy-foundations.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- policy validation/evaluation/lifecycle marker contracts
|   +-- Definitions/
|   |   +-- builder support for definition-attached policy declarations
|   +-- Models/Definitions/
|   |   +-- ResourceDefinition policy declaration metadata
|   +-- Models/Instances/
|   |   +-- lifecycle marker state and lifecycle marker write results
|   +-- Models/Policies/
|   |   +-- policy declarations, criteria, preview results, diagnostics
|   +-- Models/Portability/
|   |   +-- portable lifecycle marker state
|   +-- Models/Querying/
|   |   +-- explicit lifecycle-state query criteria
|   +-- InMemory/
|   |   +-- in-memory policy preview and lifecycle marker storage behavior
|   +-- Services/
|       +-- policy validator, preview service, lifecycle marker service
|
+-- persistence/Aster.Persistence.SqliteJson/
|   +-- SQLite JSON policy declaration persistence and lifecycle marker storage/query support
|
+-- apps/Aster.Web/
    +-- no feature-specific host UI; compatibility updates only if needed

test/
+-- Aster.Tests/
    +-- Policies/
    +-- InMemory/
    +-- Querying/
    +-- Portability/
    +-- SqliteJson/
    +-- Tenancy/
```

**Structure Decision**: Keep policy contracts in `Aster.Core` because declarations, validation, previews, lifecycle marker state, queries, portability, and tenant boundaries are SDK semantics. Provider packages implement storage and query translation behind existing provider boundaries. Do not add a policy engine package, registry, background runner, authorization layer, or new dependency.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Attach policy declarations to resource definitions only.
- Model lifecycle markers as provider-stored additive state, not resource version mutations.
- Require explicit host-supplied evaluation timestamps for age-based previews.
- Limit policy criteria to age/count/activation/lifecycle/definition/tenant criteria.
- Keep pruning preview-only in this slice.
- Extend the existing portable query model with lifecycle-state criteria instead of public SQL or `IQueryable`.
- Preserve provider agnosticism through small core contracts and local in-memory/SQLite implementations.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/policy-foundations.md](contracts/policy-foundations.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes SDK contracts only and leaves scheduling, compliance decisions, and authorization to hosts.
- **Immutable versioning**: PASS - Marker writes do not rewrite resources or resource versions.
- **Channel activation**: PASS - Activation is a criterion, not a policy side effect.
- **Typed/queryable**: PASS - Lifecycle-state filtering is an explicit query model addition; raw SQL and public `IQueryable` remain out of scope.
- **Provider agnostic**: PASS - Core services depend on abstractions; SQLite details stay in the SQLite provider.
- **Simplicity first**: PASS - The design implements current declaration/preview/marker needs without a general policy engine.
- **Modern C# idioms**: PASS - Records and explicit async service contracts fit existing code.
- **Readability over cleverness**: PASS - Direct request/result models replace hidden conventions and dynamic execution.
- **Explicitness over magic**: PASS - Host-supplied timestamps, explicit marker operations, and explicit query criteria avoid ambient behavior.
- **Abstractions justified**: PASS - Each new contract maps to a concrete cross-provider need: validate, preview, read/write marker state.
- **Optimize for deletion**: PASS - Policy declarations and marker state are additive modules with minimal coupling to resource version storage.
- **Composition over inheritance**: PASS - Data-oriented records and services compose with existing stores.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No background worker, migration framework, external service, or deployment-time policy runner is introduced.
