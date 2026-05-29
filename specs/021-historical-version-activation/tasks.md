# Tasks: Historical Version Activation

**Input**: Design documents from `/specs/021-historical-version-activation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by the feature specification for historical activation, single-active/multi-active behavior, tenant isolation, lifecycle hooks, SQLite parity, and existing compatibility.

**Organization**: Tasks are grouped by user story to keep the P1 historical activation path independently testable before the P2 safety coverage.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature context and repository guardrails before implementation.

- [X] T001 Verify feature documents and checklist are present in specs/021-historical-version-activation/
- [X] T002 Confirm no new dependency, schema, provider registry, scheduler, public SQL, or public IQueryable<Resource> work is required by specs/021-historical-version-activation/plan.md
- [X] T003 Inspect existing activation implementation and tests in src/core/Aster.Core/Services/DefaultResourceManager.cs, src/core/Aster.Core/InMemory/InMemoryResourceManager.cs, and test/Aster.Tests/

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Identify and update stale public contract language before story behavior changes.

- [X] T004 Update activation contract documentation in src/core/Aster.Core/Abstractions/IResourceManager.cs to describe any existing version as a valid activation target
- [X] T005 Update activation docs in src/core/Aster.Core/README.md to remove latest-only activation wording and describe historical activation behavior

**Checkpoint**: Public docs no longer contradict the intended implementation.

---

## Phase 3: User Story 1 - Activate Historical Version (Priority: P1) MVP

**Goal**: Hosts can activate a historical non-latest version while latest remains unchanged.

**Independent Test**: Create versions 1 and 2, activate version 1 after version 2 exists, and verify version 1 is active while version 2 remains latest.

### Tests for User Story 1

- [X] T006 [P] [US1] Update direct in-memory tests for non-latest activation in test/Aster.Tests/InMemory/InMemoryActivationTests.cs
- [X] T007 [P] [US1] Add provider-backed historical activation tests for single-active and multi-active behavior in test/Aster.Tests/InMemory/InMemoryActivationTests.cs
- [X] T008 [P] [US1] Update quickstart integration expectations for historical activation in test/Aster.Tests/Integration/QuickstartIntegrationTest.cs

### Implementation for User Story 1

- [X] T009 [US1] Remove latest-only concurrency rejection from src/core/Aster.Core/Services/DefaultResourceManager.cs while preserving version-not-found validation
- [X] T010 [US1] Remove latest-only concurrency rejection from src/core/Aster.Core/InMemory/InMemoryResourceManager.cs while preserving version-not-found validation
- [X] T011 [US1] Run focused historical activation tests for test/Aster.Tests/InMemory/InMemoryActivationTests.cs and test/Aster.Tests/Integration/QuickstartIntegrationTest.cs

**Checkpoint**: Historical activation works for the core manager paths and quickstart behavior.

---

## Phase 4: User Story 2 - Preserve Safety and Existing Boundaries (Priority: P2)

**Goal**: Historical activation keeps existing missing-version failures, tenant scoping, lifecycle hook behavior, SQLite persistence, and compatibility.

**Independent Test**: Attempt a missing historical version, activate matching resource IDs in separate tenants, and verify lifecycle hook contexts contain the requested historical version and resulting active set.

### Tests for User Story 2

- [X] T012 [P] [US2] Add missing-version fail-closed coverage for historical activation in test/Aster.Tests/InMemory/InMemoryActivationTests.cs
- [X] T013 [P] [US2] Add tenant isolation coverage for historical activation in test/Aster.Tests/Tenancy/TenantActivationTests.cs
- [X] T014 [P] [US2] Add lifecycle hook context coverage for historical activation in test/Aster.Tests/Lifecycle/LifecycleActivationHookTests.cs
- [X] T015 [P] [US2] Add SQLite historical activation persistence coverage in test/Aster.Tests/SqliteJson/SqliteJsonResourceStoreTests.cs

### Implementation for User Story 2

- [X] T016 [US2] Adjust any stale compatibility assertions that still expect non-latest activation to throw ConcurrencyException in test/Aster.Tests/
- [X] T017 [US2] Run focused tenant, lifecycle, and SQLite historical activation tests from test/Aster.Tests/

**Checkpoint**: Safety and provider parity coverage is complete.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Keep roadmap/context current and validate the whole solution.

- [X] T018 [P] Update docs/ExecutionRoadmap.md with the 021 historical activation slice status
- [X] T019 [P] Ensure AGENTS.md includes 021 technology and recent-change context
- [X] T020 Validate quickstart behavior against specs/021-historical-version-activation/quickstart.md
- [X] T021 Run dotnet test Aster.sln
- [X] T022 Run dotnet build Aster.sln /m:1
- [X] T023 Run git diff --check
- [X] T024 Review implementation against specs/021-historical-version-activation/spec.md and mark all completed tasks in specs/021-historical-version-activation/tasks.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on setup and blocks user story work.
- **User Story 1 (Phase 3)**: Depends on foundational documentation alignment.
- **User Story 2 (Phase 4)**: Depends on User Story 1 behavior change.
- **Polish (Phase 5)**: Depends on both user stories.

### User Story Dependencies

- **User Story 1 (P1)**: MVP and required before safety parity can be meaningfully validated.
- **User Story 2 (P2)**: Extends coverage around the implemented behavior without adding new public APIs.

### Parallel Opportunities

- T006, T007, and T008 can be drafted independently before implementation.
- T012, T013, T014, and T015 cover different safety dimensions and can be drafted independently.
- T018 and T019 touch separate documentation files.

---

## Implementation Strategy

### MVP First

1. Complete setup and foundational docs.
2. Update tests that currently encode latest-only activation.
3. Remove latest-only concurrency checks from both manager implementations.
4. Validate historical activation for in-memory and quickstart paths.

### Incremental Delivery

1. Add safety coverage for missing versions, tenant isolation, lifecycle hooks, and SQLite.
2. Update roadmap and agent context.
3. Run full test/build/diff validation.
