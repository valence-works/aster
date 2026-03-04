# Implementation Plan: 001-core-sdk-foundation

**Branch**: `001-core-sdk-foundation` | **Date**: 2026-03-04 | **Spec**: [specs/001-core-sdk-foundation/spec.md]
**Input**: Feature specification from `specs/001-core-sdk-foundation/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

This feature implements the foundational SDK and In-Memory engine for Aster. It includes core domain models (definitions, instances), a definition registry, versioning mechanics, and a typed aspect system. The goal is to allow developers to define, create, version, and query resources without external persistence dependencies, using an in-memory store for Phase 1.

## Technical Context

**Language/Version**: C# / .NET 10 SDK (Multitargeting: `net8.0;net9.0;net10.0`)
**Primary Dependencies**: Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging  
**Storage**: In-Memory (ConcurrentDictionary) for Phase 1  
**Testing**: xUnit (Aster.Tests), NSubstitute (see research.md §1)  
**Target Platform**: Cross-platform (Linux, Windows, macOS)  
**Project Type**: SDK Library  
**Performance Goals**: Low overhead for in-memory operations  
**Constraints**: No external database dependencies for Phase 1. Must be provider agnostic.  
**Scale/Scope**: Core foundation, extensible architecture.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **SDK-First & Headless**: The design focuses on `IResourceDefinitionBuilder`, `IResourceQueryService` and does not mention UI.
- [x] **Immutable Versioning**: Spec requires "Update operations always result in a new ResourceVersion entry" and "Versions must be immutable".
- [x] **Channel-Based Activation**: Spec requires "activations accepts allowMultipleActive flag" and "Support activating a specific version in a named channel".
- [x] **Typed & Queryable**: Spec explicitly mentions "Using Typed Aspects (POCOs)" and "ResourceQuery ... translates ... to LINQ".
- [x] **Provider Agnostic**: Spec mentions "In-Memory provider" and abstractions like `IResourceWriteStore`.
- [x] **Coding Standards**: Must adhere to `docs/coding-conventions.md`.

## Project Structure

### Documentation (this feature)

```text
specs/001-core-sdk-foundation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
src/
├── core/
│   ├── Aster.Core/           # Main SDK project
│   │   ├── Abstractions/     # Interfaces (IResourceDefinitionStore, etc.)
│   │   ├── Models/           # Domain models (Resource, Version, Aspects)
│   │   ├── Definitions/      # Definition models (ResourceDefinition, etc.)
│   │   ├── Services/         # Core logic (ResourceManager, Versioning)
│   │   ├── InMemory/         # In-Memory implementation
│   │   └── Extensions/       # DI Extensions
```

**Structure Decision**: Create `Aster.Core` project under `src/core/`. This will contain the focus of this feature.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**
