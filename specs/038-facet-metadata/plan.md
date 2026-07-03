# Implementation Plan: Facet Validation and UI Metadata

**Branch**: `038-facet-metadata` | **Date**: 2026-07-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/038-facet-metadata/spec.md`

## Summary

Extend the existing `FacetDefinition` model with an optional, additive `FacetMetadata` declaration (label, description, display order, default value, allowed values, string length bounds, numeric range bounds, regex pattern, opaque UI hint). Add registration-time metadata self-validation (stable `facet-metadata-*` diagnostic codes) invoked by both definition stores through one shared core validator, and save-time facet value validation (stable `facet-value-*` diagnostic codes) invoked by the shared resource managers before any write. Metadata rides the existing JSON payload persistence and portable snapshot serialization unchanged (no storage schema changes); null-suppressing JSON annotations keep definitions without metadata byte-identical to today. Diagnostics are deterministically ordered by aspect key → facet id → code (ordinal).

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK (`Aster.Core`), existing SQLite JSON provider (`Aster.Persistence.SqliteJson`), `System.Text.Json` and `System.Text.RegularExpressions` from the platform, xUnit test stack; no new dependencies  
**Storage**: Existing in-memory store and SQLite JSON provider; definitions remain serialized JSON payloads — no storage schema changes  
**Testing**: xUnit through `dotnet test Aster.sln`  
**Target Platform**: .NET library / headless SDK  
**Project Type**: SDK/library  
**Performance Goals**: Validation is in-memory over already-loaded definitions/values; regex evaluation is bounded by an explicit match timeout so a save can never hang.  
**Constraints**: Additive-only — facets without metadata must register, validate, save, export, and import exactly as today; stable lowercase kebab-case diagnostic codes; deterministic diagnostic ordering (aspect key → facet id → code, ordinal); in-memory + SQLite parity; no product APIs beyond the core SDK, no provider registries, no public raw SQL, no public `IQueryable<Resource>`, no schedulers, no localization, no UI framework code, no cross-facet/cross-aspect rules, no async/remote validators, no new dependencies.  
**Scale/Scope**: One optional metadata record on `FacetDefinition`, one registration-time validator, one save-time validator, two exception types with diagnostic payloads, builder ergonomics, and a full test matrix across in-memory, SQLite, and portability paths.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Metadata is declarative data on the definition model; the UI hint is an opaque string. No UI or CMS coupling is introduced.
- **Immutable versioning**: PASS - Definitions remain append-only versions; failed saves persist nothing; save semantics for valid values are unchanged.
- **Channel activation**: PASS - Activation state is untouched; validation happens on the save path only.
- **Typed/queryable**: PASS - No query surface changes; no public `IQueryable`; typed facet semantics reuse the existing declared type descriptors.
- **Provider agnostic**: PASS - Both validators live in `Aster.Core` and are shared by the in-memory store and SQLite provider; providers only invoke the shared validator at registration.
- **Simplicity first**: PASS - One metadata record, two static validators, two exception types. No rule engine, no pipeline, no extension points.
- **Modern C# idioms**: PASS - Records with `init` properties, collection expressions, pattern matching, nullable reference types.
- **Readability over cleverness**: PASS - Explicit per-constraint checks mirroring `ResourcePolicyValidator`; no metaprogramming.
- **Explicitness over magic**: PASS - Hosts opt in per facet; no discovery, no implicit defaults injection, no hidden validation for facets that declare nothing.
- **Abstractions justified**: PASS - No new interfaces. Validators are concrete static helpers (like `TenantScopeResolver`); the shared facet value resolver addresses a demonstrated duplication seam with `IndexProjectionEvaluator` semantics.
- **Optimize for deletion**: PASS - Metadata is one nullable property plus two validators with narrow call sites; removal would be localized.
- **Composition over inheritance**: PASS - Plain records and static helpers; no hierarchies.
- **Intentional dependencies**: PASS - Platform `System.Text.RegularExpressions` and `System.Text.Json` only; no new packages.
- **Operational simplicity**: PASS - No schema changes, configuration, or deployment steps; validation is `dotnet build` + `dotnet test`.

## Project Structure

### Documentation (this feature)

```text
specs/038-facet-metadata/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── facet-metadata.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/core/Aster.Core/
├── Models/Definitions/
│   ├── FacetDefinition.cs            # + optional Metadata property (null = today's behavior)
│   ├── FacetMetadata.cs              # NEW: optional metadata record
│   └── FacetMetadataDiagnostics.cs   # NEW: diagnostic records + stable code constants
├── Exceptions/
│   └── FacetValidationExceptions.cs  # NEW: FacetMetadataValidationException, FacetValueValidationException
├── Services/
│   ├── FacetMetadataValidator.cs     # NEW: registration-time self-validation (static)
│   ├── FacetValueValidator.cs        # NEW: save-time value validation (static)
│   ├── AspectFacetValueResolver.cs   # NEW (internal): facet value extraction (dictionary/JsonElement/POCO)
│   ├── DefaultResourceManager.cs     # + invoke FacetValueValidator before write (Create/Update)
│   └── (InMemory/InMemoryResourceManager.cs)  # + same invocation for parity
├── InMemory/
│   └── InMemoryResourceDefinitionStore.cs     # + invoke FacetMetadataValidator on register
└── Definitions/
    └── ResourceDefinitionBuilder.cs  # + WithFacetMetadata(...) applying to the last facet

src/persistence/Aster.Persistence.SqliteJson/
└── SqliteJsonResourceStore.cs        # + invoke FacetMetadataValidator on register (same shared validator)

test/Aster.Tests/
├── Facets/
│   ├── FacetMetadataModelTests.cs        # US1: declaration, round-trip via stores, absence preserved
│   ├── FacetMetadataValidationTests.cs   # US2: every facet-metadata-* code + ordering
│   ├── FacetValueValidationTests.cs      # US3: every facet-value-* code + ordering + nothing persisted
│   └── FacetMetadataPortabilityTests.cs  # US4: portable snapshot round-trip + no-metadata snapshot parity
└── SqliteJson/
    └── SqliteJsonFacetMetadataTests.cs   # US4: SQLite persistence/registration/save parity matrix
```

**Structure Decision**: All production logic lands in `Aster.Core` so both providers share one implementation; the SQLite provider only adds a call to the shared registration validator, guaranteeing parity by construction. Tests get a new `Facets/` area mirroring existing per-capability test folders, plus one SQLite parity fixture beside the other `SqliteJson*` tests.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research (research.md) resolves: where metadata lives on the model (nested optional record with null-suppressing JSON serialization for byte-identical no-metadata payloads), where registration validation hooks in (shared static validator called by both `RegisterDefinitionAsync` implementations; portable import stays lossless and unvalidated), where save validation hooks in (both `IResourceManager` implementations before hooks/write), how facet values are resolved from aspect payloads (same dictionary/JsonElement/POCO semantics as `IndexProjectionEvaluator`), typed comparison semantics per declared type, safe bounded regex evaluation, and handling of values that cannot be normalized to the declared type.

## Phase 1 Design Summary

Design produces data-model.md (FacetMetadata, diagnostic records, constraint applicability matrix, validation rules), contracts/facet-metadata.md (public surface, diagnostic code contract, ordering contract, non-goals), quickstart.md (declaring metadata, focused/full test commands), and the updated agent context reference in AGENTS.md.

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design adds data + validators only; UI hint remains opaque.
- **Immutable versioning**: PASS - Failed registrations/saves persist nothing; append-only semantics unchanged.
- **Channel activation**: PASS - Untouched.
- **Typed/queryable**: PASS - No query model changes.
- **Provider agnostic**: PASS - One shared validator; providers converge by construction; SQLite payload shape unchanged.
- **Simplicity first**: PASS - Two static validators, one internal resolver, one record, two exceptions; no pipelines or registries.
- **Modern C# idioms**: PASS - Records, pattern matching, collection expressions.
- **Readability over cleverness**: PASS - Straight-line per-constraint checks with explicit codes.
- **Explicitness over magic**: PASS - Opt-in metadata; documented codes; no hidden behavior for undeclared facets.
- **Abstractions justified**: PASS - No new interfaces or extension points.
- **Optimize for deletion**: PASS - Narrow call sites; feature removable by deleting the record, validators, and call lines.
- **Composition over inheritance**: PASS - No hierarchies.
- **Intentional dependencies**: PASS - Platform-only.
- **Operational simplicity**: PASS - No schema/config/deploy changes.
