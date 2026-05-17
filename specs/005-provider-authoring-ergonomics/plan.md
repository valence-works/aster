# Implementation Plan: Provider Authoring Ergonomics

**Branch**: `005-provider-authoring-ergonomics` | **Date**: 2026-05-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-provider-authoring-ergonomics/spec.md`

## Summary

Make custom query provider authoring easier and harder to misconfigure by adding a small explicit DI registration helper for active query providers and matching capability providers, improving fail-closed validation diagnostics, and documenting the minimum provider-authoring pattern. The helper registers its concrete provider types and shared interfaces as singletons; hosts that need alternate lifetimes continue to use explicit manual DI registration. The design keeps the existing provider identity/capability model, avoids registries or runtime scanning, and preserves built-in in-memory and SQLite JSON provider behavior.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; no new third-party dependencies  
**Storage**: N/A; no persistence format or storage behavior changes  
**Testing**: xUnit via `dotnet test Aster.sln`  
**Target Platform**: .NET library usable from Generic Host / ASP.NET Core hosts  
**Project Type**: SDK/library with provider package  
**Performance Goals**: Provider registration remains constant-time DI setup work; validation continues to select one capability declaration before in-memory AST validation  
**Constraints**: No provider registry, runtime scanning, public raw SQL, public `IQueryable<Resource>`, query planner, or provider-specific query construction contract  
**Scale/Scope**: Current scope covers custom query provider registration ergonomics, singleton-only helper behavior, fail-closed diagnostic text, provider authoring tests, and docs/quickstart updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS — Adds SDK DI helpers, diagnostics, tests, and docs only; no UI/CMS coupling.
- **Immutable versioning**: PASS — Query provider registration does not alter resource mutation/version semantics.
- **Channel activation**: PASS — Activation remains a query scope/capability concern.
- **Typed/queryable**: PASS — Preserves portable `ResourceQuery`; explicitly avoids public `IQueryable<Resource>` and raw SQL contracts.
- **Provider agnostic**: PASS — Core helper depends on provider-neutral abstractions, not SQLite or another database implementation.
- **Simplicity first**: PASS — A small explicit DI helper satisfies the current ergonomics need without a registry/framework.
- **Modern C# idioms**: PASS — Generic constraints and extension methods fit existing .NET SDK conventions.
- **Readability over cleverness**: PASS — Registration remains visible and explicit in host code.
- **Explicitness over magic**: PASS — No scanning/discovery; provider identity and capabilities are declared in code.
- **Abstractions justified**: PASS — The helper removes demonstrated repeated/misordered provider registration without introducing a broad new abstraction.
- **Optimize for deletion**: PASS — Helper and docs are localized; manual registration remains possible.
- **Composition over inheritance**: PASS — Uses interfaces and DI composition, no inheritance hierarchy.
- **Intentional dependencies**: PASS — No new dependencies.
- **Operational simplicity**: PASS — No deployment or infrastructure changes; local diagnostics improve.

## Project Structure

### Documentation (this feature)

```text
specs/005-provider-authoring-ergonomics/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── provider-authoring-ergonomics.md
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
│       │   └── IResourceQueryService.cs
│       ├── Extensions/
│       │   └── AsterCoreServiceCollectionExtensions.cs
│       └── Services/
│           └── ResourceQueryValidator.cs
test/
└── Aster.Tests/
    └── Querying/
        ├── ProviderAuthoringTests.cs
        └── ResourceQueryValidatorTests.cs
wiki/
├── DI-Registration.md
├── Exception-Reference.md
└── Querying.md
```

**Structure Decision**: Keep the registration helper in `Aster.Core.Extensions` beside `AddAsterCore()` because it is provider-neutral SDK hosting glue. Keep diagnostics in `ResourceQueryValidator`. Add tests to existing querying test files to avoid a new test project.

## Phase 0: Research

Completed in [research.md](research.md).

## Phase 1: Design & Contracts

Completed artifacts:

- [data-model.md](data-model.md)
- [contracts/provider-authoring-ergonomics.md](contracts/provider-authoring-ergonomics.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS — Public surface remains SDK DI/helper contracts and documentation.
- **Immutable versioning**: PASS — No mutation/storage changes.
- **Channel activation**: PASS — No activation behavior changes.
- **Typed/queryable**: PASS — Custom providers still receive `ResourceQuery`; no public queryable/raw SQL escape hatch.
- **Provider agnostic**: PASS — Helper is generic over existing query provider abstractions.
- **Simplicity first**: PASS — Single helper plus diagnostics is the smallest selected affordance.
- **Modern C# idioms**: PASS — Uses generic constraints and extension methods already familiar to .NET developers.
- **Readability over cleverness**: PASS — Host code explicitly names the query provider and capability provider.
- **Explicitness over magic**: PASS — No scanning, no implicit discovery, no hidden provider registry.
- **Abstractions justified**: PASS — Helper prevents demonstrated active-provider/capability misregistration.
- **Optimize for deletion**: PASS — Removing the helper does not affect manual registration or provider contracts.
- **Composition over inheritance**: PASS — Composition via DI registrations.
- **Intentional dependencies**: PASS — No new dependencies.
- **Operational simplicity**: PASS — Same hosting/deployment model; clearer local failures.

## Complexity Tracking

No constitution violations identified.
