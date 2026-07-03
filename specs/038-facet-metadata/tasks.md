# Tasks: Facet Validation and UI Metadata

**Input**: Design documents from `/specs/038-facet-metadata/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/facet-metadata.md, quickstart.md

**Tests**: Required. SC-002 and SC-003 mandate test coverage for every stable diagnostic code, SC-004 requires all pre-existing tests to pass unmodified, and SC-005/SC-006 require portability and provider-parity test matrices.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- Core SDK: `src/core/Aster.Core/`
- SQLite JSON provider: `src/persistence/Aster.Persistence.SqliteJson/`
- Tests: `test/Aster.Tests/` (new `Facets/` area plus the existing `SqliteJson/` area)

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm scope and study the existing seams the feature hooks into. No project scaffolding is needed — all work lands in existing projects with no new dependencies.

- [ ] T001 Confirm feature context `038-facet-metadata` and re-read design artifacts in `specs/038-facet-metadata/` (spec.md, plan.md, research.md, data-model.md, contracts/facet-metadata.md, quickstart.md)
- [ ] T002 [P] Review the existing facet model and builder seams: `src/core/Aster.Core/Models/Definitions/FacetDefinition.cs` and `src/core/Aster.Core/Definitions/ResourceDefinitionBuilder.cs` (identity, `Type` descriptor, `IsRequired`, `WithTypedFacet<T>` last-facet targeting)
- [ ] T003 [P] Review the registration and save gates plus value-resolution precedent: `src/core/Aster.Core/InMemory/InMemoryResourceDefinitionStore.cs`, `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs` (tenant-scoped `RegisterDefinitionAsync` overloads and `Serialize` JSON options), `src/core/Aster.Core/Services/DefaultResourceManager.cs`, `src/core/Aster.Core/InMemory/InMemoryResourceManager.cs` (create/update before-hook flow), and `src/core/Aster.Core/Services/IndexProjectionEvaluator.cs` (`TryResolveFacetValue` dictionary/JsonElement/POCO semantics)
- [ ] T004 Confirm Constitution Check gates in `specs/038-facet-metadata/plan.md` still pass before implementation begins (no new interfaces, registries, dependencies, or schema changes)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The optional metadata record and its byte-identical JSON behavior — every user story depends on the model existing and on no-metadata definitions serializing exactly as today.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T005 Create `FacetMetadata` record in `src/core/Aster.Core/Models/Definitions/FacetMetadata.cs` with all-optional `init` properties per data-model.md: `Label` (`string?`), `Description` (`string?`), `DisplayOrder` (`int?`), `DefaultValue` (`object?`), `AllowedValues` (`IReadOnlyList<object>?`), `MinLength`/`MaxLength` (`int?`), `Minimum`/`Maximum` (`decimal?`), `Pattern` (`string?`), `UiHint` (`string?`); annotate every nullable property with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`
- [ ] T006 Add optional `Metadata` property (`FacetMetadata?`, default `null`) to `FacetDefinition` in `src/core/Aster.Core/Models/Definitions/FacetDefinition.cs`, annotated `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, leaving identity, `Type`, and `IsRequired` semantics untouched
- [ ] T007 Run `dotnet build Aster.sln /m:1` and `dotnet test Aster.sln` to confirm the additive model change keeps all pre-existing tests passing with unchanged outcomes (SC-004 baseline)

**Checkpoint**: The metadata model exists, `null` metadata is invisible in JSON, and the existing suite is green — user story implementation can begin.

---

## Phase 3: User Story 1 - Declare Facet Metadata (Priority: P1) 🎯 MVP

**Goal**: Hosts declare optional display and validation metadata directly on facet definitions and read every declared property back through the definition store; definitions without metadata behave exactly as today.

**Independent Test**: Build a definition whose facets carry metadata, register it, read it back through the definition store, and verify every metadata property is preserved exactly; register a definition without metadata and verify identical behavior to today, including byte-identical serialized payloads.

### Tests for User Story 1

- [ ] T008 [P] [US1] Add full-metadata declaration and round-trip test in `test/Aster.Tests/Facets/FacetMetadataModelTests.cs`: register a definition whose facet declares label, description, display order, default value, allowed values, length bounds, numeric range, pattern, and UI hint via the builder, read it back through `InMemoryResourceDefinitionStore`, and assert every property is returned exactly as declared (US1 scenario 1)
- [ ] T009 [P] [US1] Add partial-metadata preservation test in `test/Aster.Tests/Facets/FacetMetadataModelTests.cs`: declare only a label and a UI hint, read back, and assert declared properties are preserved and all undeclared properties are `null` (US1 scenario 3)
- [ ] T010 [P] [US1] Add no-metadata parity test in `test/Aster.Tests/Facets/FacetMetadataModelTests.cs`: register a definition without metadata, assert `FacetDefinition.Metadata` is `null` after read-back, and assert the JSON serialization of the definition (web defaults, matching `SqliteJsonResourceStore.Serialize`) contains no `metadata` property — byte-identical to today (US1 scenario 2, FR-002)
- [ ] T011 [P] [US1] Add builder guard test in `test/Aster.Tests/Facets/FacetMetadataModelTests.cs`: calling `WithFacetMetadata(...)` before any facet has been added throws `InvalidOperationException`

### Implementation for User Story 1

- [ ] T012 [US1] Add `WithFacetMetadata(FacetMetadata metadata)` to `src/core/Aster.Core/Definitions/ResourceDefinitionBuilder.cs`, attaching metadata to the most recently added facet of the most recently added aspect (mirroring `WithTypedFacet<T>` targeting) and throwing `InvalidOperationException` when no facet exists; keep all existing method signatures unchanged

**Checkpoint**: Hosts can declare, register, and read back facet metadata; no-metadata definitions are unchanged. MVP deliverable complete.

---

## Phase 4: User Story 2 - Reject Invalid Metadata at Registration (Priority: P2)

**Goal**: Both definition stores reject invalid metadata combinations at registration through one shared static validator, throwing `FacetMetadataValidationException` with all seven stable `facet-metadata-*` diagnostic codes, deterministically ordered.

**Independent Test**: Attempt to register definitions carrying each invalid metadata combination and verify each is rejected with its expected stable diagnostic code (and offending aspect key + facet id); register valid metadata and verify success.

### Tests for User Story 2

- [ ] T013 [P] [US2] Add range-invalid and constraint-inapplicable tests in `test/Aster.Tests/Facets/FacetMetadataValidationTests.cs`: `MinLength > MaxLength`, negative length bounds, `Minimum > Maximum`, non-whole numeric bound on an int facet (all `facet-metadata-range-invalid`); length/pattern on non-string facets, numeric range on non-numeric facets (including datetime), allowed values on bool facets (all `facet-metadata-constraint-inapplicable`) per the data-model.md applicability matrix
- [ ] T014 [P] [US2] Add allowed-values tests in `test/Aster.Tests/Facets/FacetMetadataValidationTests.cs`: empty list rejected as `facet-metadata-allowed-values-empty`; duplicates after normalization (e.g., `1` vs `1.0` on a decimal facet, two ISO spellings of one datetime) rejected as `facet-metadata-allowed-values-duplicate`; an entry that does not normalize to the declared type rejected as `facet-metadata-allowed-value-type-mismatch`
- [ ] T015 [P] [US2] Add default-value and pattern tests in `test/Aster.Tests/Facets/FacetMetadataValidationTests.cs`: default value violating type, allowed values, length, range, or pattern rejected as `facet-metadata-default-value-invalid`; a syntactically invalid regex rejected as `facet-metadata-pattern-invalid`
- [ ] T016 [P] [US2] Add aggregation, ordering, and success tests in `test/Aster.Tests/Facets/FacetMetadataValidationTests.cs`: a definition with multiple facets across multiple aspects carrying multiple errors reports every violation together, ordered by aspect key → facet id → code (ordinal), each diagnostic carrying `AspectKey` and `FacetDefinitionId` (FR-010); repeated validation yields identical output (SC-007); fully valid metadata (including equal min/max bounds) registers successfully; and rejection occurs before any state change (definition not retrievable afterwards)

### Implementation for User Story 2

- [ ] T017 [P] [US2] Create `FacetMetadataDiagnostic` record and `FacetMetadataDiagnosticCodes` constants (`facet-metadata-range-invalid`, `facet-metadata-constraint-inapplicable`, `facet-metadata-allowed-values-empty`, `facet-metadata-allowed-values-duplicate`, `facet-metadata-allowed-value-type-mismatch`, `facet-metadata-default-value-invalid`, `facet-metadata-pattern-invalid`) in `src/core/Aster.Core/Models/Definitions/FacetMetadataDiagnostics.cs`, each diagnostic carrying `Code`, `Message`, `AspectKey`, `FacetDefinitionId`
- [ ] T018 [P] [US2] Create `FacetMetadataValidationException` (carrying ordered `IReadOnlyList<FacetMetadataDiagnostic> Diagnostics`) in `src/core/Aster.Core/Exceptions/FacetValidationExceptions.cs`
- [ ] T019 [US2] Create static `FacetMetadataValidator` in `src/core/Aster.Core/Services/FacetMetadataValidator.cs` (following the `TenantScopeResolver` static-helper precedent): iterate every `(aspectKey, aspectDefinition)` and metadata-bearing facet, enforce the constraint applicability matrix, bound coherence (min ≤ max, non-negative lengths, whole int bounds), allowed-values non-empty/distinct-after-normalization/type-matched, pattern well-formedness (`new Regex(pattern)` mapping `ArgumentException` to the pattern-invalid code), and default-value conformance against type plus all declared constraints; include internal type-normalization helpers per research.md (string ordinal, int→`long`, `decimal`, `bool`, ISO 8601 `datetime`, `JsonElement` tolerant) shared with the save-time validator; collect all violations, sort by `AspectKey` → `FacetDefinitionId` → `Code` with `StringComparer.Ordinal`, and throw `FacetMetadataValidationException` when any exist
- [ ] T020 [US2] Invoke `FacetMetadataValidator` at the top of the tenant-scoped `RegisterDefinitionAsync` in `src/core/Aster.Core/InMemory/InMemoryResourceDefinitionStore.cs`, before any state change
- [ ] T021 [US2] Invoke the same `FacetMetadataValidator` at the top of the tenant-scoped `RegisterDefinitionAsync` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`, before any write; do NOT invoke it from portable-import paths (`ResourcePortabilityService` / `ImportDefinitionVersion` stay lossless per research.md)

**Checkpoint**: All seven `facet-metadata-*` codes are enforced identically by both stores at registration, deterministically ordered; import remains unvalidated and lossless.

---

## Phase 5: User Story 3 - Enforce Metadata Constraints on Save (Priority: P3)

**Goal**: Both resource managers validate present facet values against declared constraints before lifecycle hooks and before any write, throwing `FacetValueValidationException` with the four stable `facet-value-*` codes; nothing is persisted on failure and missing-value handling stays governed solely by `IsRequired`.

**Independent Test**: Save resource versions whose facet values violate each constraint kind and verify each save fails with the expected stable diagnostic code; save conforming and boundary values and verify success; save values for facets without metadata and verify behavior unchanged from today.

### Tests for User Story 3

- [ ] T022 [P] [US3] Add per-constraint violation tests in `test/Aster.Tests/Facets/FacetValueValidationTests.cs`: value outside `AllowedValues` fails `facet-value-not-allowed`; string shorter than `MinLength` or longer than `MaxLength` fails `facet-value-length-out-of-range`; numeric value below `Minimum` or above `Maximum` fails `facet-value-out-of-range` (integer comparison for int facets, decimal for decimal facets); string not matching `Pattern` fails `facet-value-pattern-mismatch` — each on both `CreateAsync` and `UpdateAsync`
- [ ] T023 [P] [US3] Add success-path and edge-case tests in `test/Aster.Tests/Facets/FacetValueValidationTests.cs`: conforming values save exactly as today; boundary values (length equal to min/max, numeric value equal to min/max, equal min==max bounds) pass; a value satisfying allowed values but violating pattern/range still fails (all declared constraints enforced together); absent/null values skip all constraint checks with `IsRequired` semantics unchanged (FR-015); facets without metadata and facets with display-only metadata validate exactly as today (FR-017); values that cannot be normalized to the declared type are skipped and save as today (research.md); default values are never injected into saved versions (FR-018)
- [ ] T024 [P] [US3] Add aggregation, ordering, atomicity, and payload-shape tests in `test/Aster.Tests/Facets/FacetValueValidationTests.cs`: a resource version with multiple violating values across multiple aspects reports all violations together ordered by aspect key → facet id → code (ordinal), each carrying `AspectKey` and `FacetDefinitionId`; on failure nothing is persisted and no before-save lifecycle hook ran (FR-016, contract); violations are detected across dictionary, `JsonElement`, and typed-POCO aspect payload shapes

### Implementation for User Story 3

- [ ] T025 [P] [US3] Create `FacetValueDiagnostic` record and `FacetValueDiagnosticCodes` constants (`facet-value-not-allowed`, `facet-value-length-out-of-range`, `facet-value-out-of-range`, `facet-value-pattern-mismatch`) in `src/core/Aster.Core/Models/Definitions/FacetMetadataDiagnostics.cs`
- [ ] T026 [US3] Add `FacetValueValidationException` (carrying ordered `IReadOnlyList<FacetValueDiagnostic> Diagnostics`) to `src/core/Aster.Core/Exceptions/FacetValidationExceptions.cs`
- [ ] T027 [P] [US3] Create internal static `AspectFacetValueResolver` in `src/core/Aster.Core/Services/AspectFacetValueResolver.cs` resolving a facet value from an aspect payload via `IDictionary` entries, `JsonElement` properties, and POCO properties (case-insensitive), including `JsonElement` → CLR conversion, mirroring `IndexProjectionEvaluator.TryResolveFacetValue` semantics
- [ ] T028 [US3] Create static `FacetValueValidator` in `src/core/Aster.Core/Services/FacetValueValidator.cs`: for each aspect attachment with a matching payload on the resource, resolve each metadata-bearing facet's value via `AspectFacetValueResolver`; skip absent/null and unnormalizable values; check present normalized values against `AllowedValues`, length bounds, numeric range, and pattern using the shared normalization helpers; evaluate patterns with `Regex.IsMatch(value, pattern, RegexOptions.None, matchTimeout)` using a 1-second timeout and treat `RegexMatchTimeoutException` as deterministic `facet-value-pattern-mismatch`; sort diagnostics by `AspectKey` → `FacetDefinitionId` → `Code` (ordinal) and return them
- [ ] T029 [US3] Invoke `FacetValueValidator` in `src/core/Aster.Core/Services/DefaultResourceManager.cs` `CreateAsync` and `UpdateAsync`, after resolving the definition and before the before-save lifecycle hook and any write, throwing `FacetValueValidationException` when violations exist (definition or metadata absent ⇒ no checks, behavior unchanged)
- [ ] T030 [US3] Add the same invocation to `CreateAsync` and `UpdateAsync` in `src/core/Aster.Core/InMemory/InMemoryResourceManager.cs` for identical save semantics

**Checkpoint**: All four `facet-value-*` codes are enforced on save before hooks and writes; failed saves persist nothing; unconstrained facets behave exactly as today.

---

## Phase 6: User Story 4 - Portable and Provider-Neutral Metadata (Priority: P4)

**Goal**: Metadata round-trips losslessly through portable snapshot export/import, and the in-memory store and SQLite JSON provider exhibit identical persistence, registration, and save-validation behavior with zero SQLite schema changes.

**Independent Test**: Export a definition with full metadata, import into a fresh store, and verify metadata equivalence; run the registration and save-validation matrix against both providers and verify identical outcomes and diagnostic codes.

### Tests for User Story 4

- [ ] T031 [P] [US4] Add portable snapshot round-trip tests in `test/Aster.Tests/Facets/FacetMetadataPortabilityTests.cs`: export a registered definition with full metadata via `ResourcePortabilityService`, import into a fresh store, and assert every metadata property is preserved losslessly (SC-005); export a definition without metadata and assert the serialized snapshot content contains no metadata property — unchanged from today (FR-019); confirm import does not re-run metadata validation (lossless, unvalidated per research.md)
- [ ] T032 [P] [US4] Add SQLite persistence and parity tests in `test/Aster.Tests/SqliteJson/SqliteJsonFacetMetadataTests.cs`: register a full-metadata definition against `SqliteJsonResourceStore` and read back every property intact through the JSON payload; assert a no-metadata definition's stored payload contains no `metadata` property; assert no SQLite schema change (table shape unchanged versus `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`)
- [ ] T033 [US4] Add provider-parity matrix tests in `test/Aster.Tests/SqliteJson/SqliteJsonFacetMetadataTests.cs`: run representative registration rejections (each `facet-metadata-*` code) and save rejections (each `facet-value-*` code) against both the in-memory configuration and the SQLite JSON configuration, asserting identical outcomes, diagnostic codes, and ordering (SC-006, FR-020)

### Implementation for User Story 4

- [ ] T034 [US4] Confirm no production changes are required for portability and parity (metadata rides the existing `ResourceDefinition` JSON serialization in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs` and `PortableSnapshot`); fix any round-trip or parity divergence surfaced by T031–T033 inside the shared core types only

**Checkpoint**: Metadata is portable and provider-neutral; both providers produce identical outcomes and codes with zero schema changes.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final verification that the whole slice satisfies the constitution, the determinism contract, and the no-regression guarantee.

- [ ] T035 [P] Re-run Constitution Check from `specs/038-facet-metadata/plan.md` against the final implementation: no new interfaces, registries, public raw SQL, public `IQueryable<Resource>`, schedulers, localization, UI code, cross-facet rules, async validators, or dependencies (FR-021)
- [ ] T036 [P] Verify XML documentation comments on all new public surface (`FacetMetadata`, `FacetDefinition.Metadata`, diagnostics, exceptions, validators, `WithFacetMetadata`) matches repository conventions in `src/core/Aster.Core/`
- [ ] T037 Run focused suites from `specs/038-facet-metadata/quickstart.md`: `dotnet test Aster.sln --filter "FullyQualifiedName~FacetMetadata|FullyQualifiedName~FacetValueValidation"` and `dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonFacetMetadataTests"`
- [ ] T038 Run full validation: `dotnet build Aster.sln /m:1` and `dotnet test Aster.sln`, confirming all pre-existing tests pass without modification to expected outcomes (SC-004)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories (every story needs the `FacetMetadata` model and null-suppressed serialization)
- **User Stories (Phases 3–6)**: All depend on Foundational completion
  - **US1 (Phase 3)**: Independent after Phase 2
  - **US2 (Phase 4)**: Independent after Phase 2 (tests may use the T012 builder method for ergonomics, but metadata can equally be set directly on `FacetDefinition` records)
  - **US3 (Phase 5)**: Independent after Phase 2; T026 edits the same file created by T018, so if US2 and US3 run in parallel, coordinate on `src/core/Aster.Core/Exceptions/FacetValidationExceptions.cs` and `src/core/Aster.Core/Models/Definitions/FacetMetadataDiagnostics.cs`; T028 reuses normalization helpers from T019
  - **US4 (Phase 6)**: Round-trip tests (T031, T032 persistence portions) depend only on Phase 2; the parity matrix (T033) depends on US2 and US3 being complete
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Foundational only — MVP
- **US2 (P2)**: Foundational only (builder from US1 is a convenience, not a requirement)
- **US3 (P3)**: Foundational; shares two files with US2 (diagnostics file, exceptions file) and reuses US2's normalization helpers — sequential execution after US2 is simplest
- **US4 (P4)**: Persistence/portability tests need only Foundational; the full parity matrix needs US2 + US3

### Within Each User Story

- Tests are written first and must fail before implementation
- Records/constants before validators; validators before store/manager invocations
- Story complete (checkpoint verified) before moving to the next priority

### Parallel Opportunities

- Phase 1: T002 and T003 in parallel
- Phase 3 (US1): T008, T009, T010, T011 (one test file, but independent test cases; safe for one author, or write together) — the test tasks can all be authored before T012
- Phase 4 (US2): T013–T016 in parallel (test authoring); T017 and T018 in parallel (different files) before T019
- Phase 5 (US3): T022–T024 in parallel; T025 and T027 in parallel; T029 and T030 in parallel after T028
- Phase 6 (US4): T031 and T032 in parallel
- Phase 7: T035 and T036 in parallel

---

## Parallel Example: User Story 2

```bash
# Author all US2 test tasks together (same new file, independent cases):
Task: "Range-invalid and constraint-inapplicable tests in test/Aster.Tests/Facets/FacetMetadataValidationTests.cs"
Task: "Allowed-values tests in test/Aster.Tests/Facets/FacetMetadataValidationTests.cs"
Task: "Default-value and pattern tests in test/Aster.Tests/Facets/FacetMetadataValidationTests.cs"
Task: "Aggregation, ordering, and success tests in test/Aster.Tests/Facets/FacetMetadataValidationTests.cs"

# Then create the independent model/exception files in parallel:
Task: "FacetMetadataDiagnostic + codes in src/core/Aster.Core/Models/Definitions/FacetMetadataDiagnostics.cs"
Task: "FacetMetadataValidationException in src/core/Aster.Core/Exceptions/FacetValidationExceptions.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (`FacetMetadata` record + `FacetDefinition.Metadata` + green baseline)
3. Complete Phase 3: US1 (builder + declaration round-trip tests)
4. **STOP and VALIDATE**: hosts can already generate forms from declared metadata (SC-001) with zero enforcement risk
5. Demo/merge if desired

### Incremental Delivery

1. Setup + Foundational → additive model in place, suite green
2. US1 → declarative metadata readable through the store (MVP)
3. US2 → registration self-validation with all seven `facet-metadata-*` codes
4. US3 → save-time enforcement with all four `facet-value-*` codes
5. US4 → portability round-trip + SQLite parity matrix
6. Polish → constitution re-check + full-suite verification

### Parallel Team Strategy

1. Team completes Setup + Foundational together
2. Then: Developer A takes US1 (builder + model tests); Developer B takes US2 (registration validator); Developer C starts US3 tests and the value resolver (coordinating with B on the shared diagnostics/exceptions files)
3. US4's parity matrix lands last, after US2 + US3 integrate

---

## Notes

- [P] tasks = different files (or independent authoring), no dependencies on incomplete tasks
- Verify each new test fails before implementing its production code
- Commit after each task or logical group
- Diagnostic codes are contract: exact lowercase kebab-case strings from contracts/facet-metadata.md — never rename
- Ordering is contract: aspect key → facet id → code, `StringComparer.Ordinal`, everywhere diagnostics are emitted
- Portable import must never invoke `FacetMetadataValidator` (lossless round-trip guarantee)
