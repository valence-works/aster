# Tasks: Provider Authoring Ergonomics

**Input**: Design documents from `/specs/005-provider-authoring-ergonomics/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/provider-authoring-ergonomics.md, quickstart.md
**Tests**: Required by FR-005, FR-006, FR-011, SC-001, SC-002, SC-004

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or does not depend on incomplete tasks
- **[Story]**: Which user story this task belongs to
- Every task includes an exact repository path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature branch and existing provider/querying surface are ready for implementation.

- [X] T001 Inspect existing query provider DI registrations in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T002 [P] Inspect existing provider identity and capability contracts in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Abstractions/IResourceQueryProviderIdentity.cs` and `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Abstractions/IResourceQueryCapabilitiesProvider.cs`
- [X] T003 [P] Inspect existing query validation diagnostics in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T004 Confirm no new dependency or storage changes are needed by checking `/Users/sipke/Projects/ValenceWorks/aster/specs/005-provider-authoring-ergonomics/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared implementation and test scaffolding before story-specific work.

**CRITICAL**: No user story work should begin until this phase is complete.

- [X] T005 Create provider-authoring test scaffolding in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T006 Add custom provider test doubles implementing `IResourceQueryService` and `IResourceQueryProviderIdentity` in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T007 Add custom capabilities provider test doubles implementing `IResourceQueryCapabilitiesProvider` in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T008 Add shared custom query builder or fixture helpers for provider-authoring tests in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`

**Checkpoint**: Test scaffolding exists and user story work can proceed.

---

## Phase 3: User Story 1 - Register A Custom Query Provider Correctly (Priority: P1) MVP

**Goal**: Provide a clear SDK path that registers a custom query provider, provider identity, and matching capabilities together.

**Independent Test**: Register a custom provider with matching capabilities and confirm validation uses the custom capabilities instead of defaults.

### Tests for User Story 1

- [X] T009 [US1] Add a failing test proving `AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>()` resolves the custom provider as active `IResourceQueryService` in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T010 [US1] Add a failing test proving the helper resolves the custom provider identity and matching capability declaration in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T011 [US1] Add a failing test proving custom capabilities are used instead of earlier in-memory defaults in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T012 [US1] Add a failing test proving the helper registers query service and capabilities provider concrete types plus shared interfaces as singletons in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`

### Implementation for User Story 1

- [X] T013 [US1] Implement `AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>()` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T014 [US1] Apply generic constraints requiring `TQueryService` to implement `IResourceQueryService` and `IResourceQueryProviderIdentity` and `TCapabilitiesProvider` to implement `IResourceQueryCapabilitiesProvider` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T015 [US1] Register the query service and capabilities provider concrete types and shared interfaces as singletons while preserving last-registration-wins behavior in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - Catch Provider Registration Mistakes Early (Priority: P1)

**Goal**: Keep validation fail-closed while making missing or mismatched capabilities actionable.

**Independent Test**: Register incomplete or mismatched custom provider pieces and confirm validation reports `capabilities-not-declared` with active provider key guidance.

### Tests for User Story 2

- [X] T016 [US2] Add a failing test for missing custom provider capabilities returning `capabilities-not-declared` in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T017 [US2] Add a failing test for mismatched provider keys returning `capabilities-not-declared` with the active provider key in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T018 [P] [US2] Update existing validator diagnostic assertions for actionable provider-key guidance in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`

### Implementation for User Story 2

- [X] T019 [US2] Update `ResourceQueryValidator` to track the active provider key from `IResourceQueryProviderIdentity` when available in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T020 [US2] Improve the `capabilities-not-declared` message for missing identity, empty provider key, and mismatched capabilities in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T021 [US2] Confirm validation still returns failures instead of throwing for missing or mismatched capabilities in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourceQueryValidator.cs`

**Checkpoint**: User Story 2 is functional and independently testable.

---

## Phase 5: User Story 3 - Reuse Validation In Custom Provider Execution (Priority: P2)

**Goal**: Document and test the provider authoring pattern where custom providers run shared validation before execution while preserving provider-specific execution authority.

**Independent Test**: Implement a custom provider test double that runs shared validation before execution and still rejects a provider-specific unsupported shape.

### Tests for User Story 3

- [X] T022 [US3] Add a failing test proving a custom provider can run shared validation before execution in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T023 [US3] Add a failing test proving provider-specific execution failures remain structured and authoritative in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`

### Implementation for User Story 3

- [X] T024 [US3] Add or adjust custom provider test-double execution logic that maps validation failures through structured unsupported-query failures in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T025 [US3] Document shared validation before execution and execution-authoritative behavior in `/Users/sipke/Projects/ValenceWorks/aster/wiki/Querying.md`
- [X] T026 [US3] Document structured provider-specific unsupported-query failures in `/Users/sipke/Projects/ValenceWorks/aster/wiki/Exception-Reference.md`

**Checkpoint**: User Story 3 is functional and independently testable.

---

## Phase 6: User Story 4 - Follow Provider Authoring Documentation (Priority: P3)

**Goal**: Provide concise provider-authoring documentation and examples that a consumer can follow without reverse-engineering built-ins.

**Independent Test**: Follow the docs to define provider identity, declare capabilities, register the provider, validate a query, and understand troubleshooting.

### Tests for User Story 4

- [X] T027 [US4] Verify the quickstart minimal registration example is represented by tests in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T028 [US4] Verify existing built-in `AddAsterCore()` and `AddAsterSqliteJson()` behavior remains unchanged in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs`

### Implementation for User Story 4

- [X] T029 [P] [US4] Document the provider registration helper and manual-registration lifetime escape hatch in `/Users/sipke/Projects/ValenceWorks/aster/wiki/DI-Registration.md`
- [X] T030 [P] [US4] Add provider-authoring guidance to the package README in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/README.md`
- [X] T031 [P] [US4] Update the feature quickstart with the singleton helper contract and troubleshooting guidance in `/Users/sipke/Projects/ValenceWorks/aster/specs/005-provider-authoring-ergonomics/quickstart.md`
- [X] T032 [P] [US4] Update the public contract document with singleton helper behavior in `/Users/sipke/Projects/ValenceWorks/aster/specs/005-provider-authoring-ergonomics/contracts/provider-authoring-ergonomics.md`

**Checkpoint**: User Story 4 is documented and independently verifiable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verify compatibility, constitution alignment, and final quality.

- [X] T033 [P] Update agent context after planning artifacts are current by running `.specify/scripts/bash/update-agent-context.sh copilot` from `/Users/sipke/Projects/ValenceWorks/aster`
- [X] T034 [P] Review implementation for unnecessary abstractions, runtime scanning, provider registry behavior, public raw SQL, and public `IQueryable<Resource>` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core`
- [X] T035 Run `dotnet test Aster.sln` from `/Users/sipke/Projects/ValenceWorks/aster`
- [X] T036 Run `dotnet build Aster.sln` from `/Users/sipke/Projects/ValenceWorks/aster`
- [X] T037 Run `git diff --check` from `/Users/sipke/Projects/ValenceWorks/aster`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories
- **US1 (Phase 3)**: Depends on Foundational; provides the registration helper
- **US2 (Phase 4)**: Depends on Foundational; can proceed independently of US1 except for helper-based test setup convenience
- **US3 (Phase 5)**: Depends on Foundational; documentation can proceed after behavior is confirmed
- **US4 (Phase 6)**: Depends on US1, US2, and US3 behavior decisions
- **Polish (Phase 7)**: Depends on all desired user stories

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on other user stories
- **User Story 2 (P1)**: No dependency on other user stories
- **User Story 3 (P2)**: Can start after Foundational, but benefits from US2 diagnostic behavior
- **User Story 4 (P3)**: Depends on finalized behavior from US1-US3

### Parallel Opportunities

- T002-T003 can run in parallel after T001.
- T018 can run in parallel with T016-T017 because it updates a separate test file.
- T029-T032 can run in parallel because they update separate documentation artifacts.
- T033-T034 can run in parallel before final verification commands.

---

## Parallel Example: User Story 2

```bash
Task: "Add a failing test for missing custom provider capabilities returning capabilities-not-declared in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ProviderAuthoringTests.cs"
Task: "Update existing validator diagnostic assertions for actionable provider-key guidance in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete User Story 1 to provide the minimal registration helper.
3. Validate User Story 1 independently with `dotnet test Aster.sln`.

### Incremental Delivery

1. Add User Story 1 for correct registration.
2. Add User Story 2 for fail-closed diagnostics.
3. Add User Story 3 for validation-before-execution guidance.
4. Add User Story 4 for docs and compatibility verification.
5. Run final build, test, and diff checks.

### Constitution Guardrails

- Keep the helper explicit and singleton-only.
- Keep manual registration supported for alternate lifetimes.
- Do not introduce a provider registry, runtime scanning, query planner, raw SQL contract, public `IQueryable<Resource>`, storage change, or new third-party dependency.
