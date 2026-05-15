# Implementation Plan: Provider Validation Execution Alignment

**Branch**: `004-provider-validation-execution` | **Date**: 2026-05-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-provider-validation-execution/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Align provider query execution with declared query capabilities and preflight validation. The design adds explicit provider keys to query providers and capability declarations, makes unsupported execution failures expose stable code/category/message data, and lets providers run shared validation before execution while keeping provider-specific safeguards authoritative.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; SQLite provider keeps existing `Microsoft.Data.Sqlite`  
**Storage**: N/A for core validation/failure contracts; existing in-memory and SQLite JSON providers keep current persistence behavior  
**Testing**: xUnit via `dotnet test Aster.sln`  
**Target Platform**: .NET library usable from Generic Host / ASP.NET Core hosts  
**Project Type**: SDK/library with provider package  
**Performance Goals**: Validation remains in-memory over a `ResourceQuery` AST and should add only small per-query overhead before provider execution; no additional database round trips are introduced by validation  
**Constraints**: No public `IQueryable<Resource>` contract; no raw SQL public surface; no silent fallback; no new third-party dependencies expected; execution remains authoritative  
**Scale/Scope**: Current scope covers existing in-memory and SQLite JSON providers, active provider/capability matching, unsupported query failure shape, provider consistency tests, and documentation updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS — Adds SDK contracts/failures and provider behavior only; no UI/CMS coupling.
- **Immutable versioning**: PASS — Query validation/execution behavior does not alter resource mutation semantics.
- **Channel activation**: PASS — Active/draft scope behavior remains represented in the query model and capability declarations.
- **Typed/queryable**: PASS — Preserves portable `ResourceQuery`; explicitly avoids public `IQueryable<Resource>` or provider-specific query construction.
- **Provider agnostic**: PASS — Core validation and failure contracts remain provider-neutral; providers declare explicit keys/capabilities.
- **Simplicity first**: PASS — Use provider keys, existing validator, and structured exception data instead of a query planner or negotiation system.
- **Modern C# idioms**: PASS — Records/properties and concise exception contracts fit the existing code style.
- **Readability over cleverness**: PASS — Explicit provider keys avoid type-name inference and hidden matching behavior.
- **Explicitness over magic**: PASS — Provider identity and failure categories are visible through code/configuration/results.
- **Abstractions justified**: PASS — Provider identity and structured execution failures address current multi-provider validation/execution consistency needs.
- **Optimize for deletion**: PASS — Changes are localized to query provider/capability/failure surfaces and can be removed without changing resource storage.
- **Composition over inheritance**: PASS — No inheritance hierarchy planned; behavior composes through services and provider declarations.
- **Intentional dependencies**: PASS — No new third-party dependencies.
- **Operational simplicity**: PASS — No infrastructure or deployment changes; debugging improves via stable failure codes/categories.

## Project Structure

### Documentation (this feature)

```text
specs/004-provider-validation-execution/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── provider-validation-execution.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── core/
│   └── Aster.Core/
│       ├── Abstractions/
│       │   ├── IResourceQueryCapabilitiesProvider.cs
│       │   ├── IResourceQueryProviderIdentity.cs
│       │   ├── IResourceQueryService.cs
│       │   └── IResourceQueryValidator.cs
│       ├── Exceptions/
│       │   └── AsterExceptions.cs
│       ├── InMemory/
│       │   ├── InMemoryQueryCapabilitiesProvider.cs
│       │   └── InMemoryQueryService.cs
│       ├── Models/
│       │   └── Querying/
│       │       ├── QueryCapabilityDescription.cs
│       │       ├── QueryValidationFailure.cs
│       │       └── QueryValidationResult.cs
│       └── Services/
│           └── ResourceQueryValidator.cs
├── persistence/
│   └── Aster.Persistence.SqliteJson/
│       ├── SqliteJsonQueryCapabilitiesProvider.cs
│       └── SqliteJsonQueryService.cs
test/
└── Aster.Tests/
    ├── InMemory/
    │   └── InMemoryQueryServiceTests.cs
    ├── Querying/
    │   ├── QueryCapabilityDiscoveryTests.cs
    │   └── ResourceQueryValidatorTests.cs
    └── SqliteJson/
        ├── SqliteJsonQueryCapabilityTests.cs
        └── SqliteJsonQueryServiceTests.cs
```

**Structure Decision**: Keep provider identity, validation, and execution failure contracts in `Aster.Core` because they are provider-neutral SDK surface. Provider-specific declarations and execution integration remain beside each provider. Tests stay in the existing provider/querying test folders to compare validation and execution behavior without creating a new test project.

## Phase 0: Research

Completed in [research.md](research.md).

## Phase 1: Design & Contracts

Completed artifacts:

- [data-model.md](data-model.md)
- [contracts/provider-validation-execution.md](contracts/provider-validation-execution.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS — Public surface remains SDK contracts, exceptions, and provider declarations.
- **Immutable versioning**: PASS — No resource persistence mutation changes introduced.
- **Channel activation**: PASS — Activation remains a query scope concern only.
- **Typed/queryable**: PASS — No public `IQueryable<Resource>` or provider-specific query construction.
- **Provider agnostic**: PASS — Core identity/failure abstractions do not depend on SQLite or in-memory implementation details.
- **Simplicity first**: PASS — Explicit provider key plus shared validator reuse is the smallest design that resolves stale capability matching and consistency.
- **Modern C# idioms**: PASS — Uses existing records/interfaces/exceptions style.
- **Readability over cleverness**: PASS — Explicit key matching replaces type-name matching.
- **Explicitness over magic**: PASS — Provider identity and unsupported failure details are discoverable.
- **Abstractions justified**: PASS — `IResourceQueryProviderIdentity` or equivalent key surface is required by current custom-provider fail-closed behavior.
- **Optimize for deletion**: PASS — Provider key/failure additions are localized to query execution and validation.
- **Composition over inheritance**: PASS — Providers compose identity/capability/validation behavior without inheritance.
- **Intentional dependencies**: PASS — No new third-party dependencies.
- **Operational simplicity**: PASS — No setup/runtime infrastructure changes.

## Complexity Tracking

No constitution violations identified.
