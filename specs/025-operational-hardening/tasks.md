# Tasks: Operational Hardening

**Input**: Design documents from `/specs/025-operational-hardening/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: This slice is test-focused. Add production fixes only if deterministic tests expose a bug.

## Phase 1: Setup

- [X] T001 Review lifecycle restore fixtures and tests in `test/Aster.Tests/Lifecycle/`
- [X] T002 [P] Review policy pruning fixtures and SQLite tests in `test/Aster.Tests/Policies/` and `test/Aster.Tests/SqliteJson/`
- [X] T003 [P] Review historical activation tests in `test/Aster.Tests/InMemory/` and `test/Aster.Tests/Tenancy/`

## Phase 2: Foundational

- [X] T004 Create operational hardening test file in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`
- [X] T005 Add shared fixture helpers for in-memory and SQLite scenarios in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`

## Phase 3: User Story 1 - Retry Restore Safely (P1)

- [X] T006 [US1] Add repeated restore retry test in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`
- [X] T007 [US1] Add concurrent same-candidate restore test in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`

## Phase 4: User Story 2 - Retry Pruning Safely (P2)

- [X] T008 [US2] Add in-memory pruning retry test in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`
- [X] T009 [US2] Add SQLite persisted pruning retry test in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`

## Phase 5: User Story 3 - Repeat Historical Activation Predictably (P3)

- [X] T010 [US3] Add repeated single-active historical activation test in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`
- [X] T011 [US3] Add repeated multi-active historical activation test in `test/Aster.Tests/Operational/OperationalHardeningTests.cs`

## Phase 6: Polish

- [X] T012 Update roadmap housekeeping in `docs/ExecutionRoadmap.md`
- [X] T013 Update `AGENTS.md` recent changes for `025-operational-hardening`
- [X] T014 Run focused hardening tests with `dotnet test Aster.sln --filter "FullyQualifiedName~OperationalHardeningTests"`
- [X] T015 Run `dotnet test Aster.sln`
- [X] T016 Run `dotnet build Aster.sln /m:1`
- [X] T017 Run `git diff --check`
- [X] T018 Re-check constitution constraints and keep production changes absent unless tests require a bug fix

## Dependencies & Execution Order

- Setup tasks precede test implementation.
- Restore hardening can run independently.
- Pruning hardening can run independently after shared fixtures.
- Activation hardening can run independently after shared fixtures.
- Polish follows focused tests.

## Notes

- Keep tests bounded and deterministic.
- Assert final state, not only result statuses.
- Do not introduce new APIs, storage schema changes, providers, schedulers, benchmark infrastructure, or dependencies.
