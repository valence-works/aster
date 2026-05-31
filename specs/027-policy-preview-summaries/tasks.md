# Tasks: Policy Preview Summaries

**Input**: Design documents from `/specs/027-policy-preview-summaries/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required by the feature specification because summary behavior is pure and independently testable.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the active slice and no dependency or provider setup is needed.

- [ ] T001 Confirm `.specify/feature.json` points to `specs/027-policy-preview-summaries`
- [ ] T002 Confirm `AGENTS.md` points to `specs/027-policy-preview-summaries/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared preview summary model file used by all stories.

- [ ] T003 [P] Add preview summary record contracts in `src/core/Aster.Core/Models/Policies/ResourcePolicyPreviewSummaries.cs`
- [ ] T004 Confirm the existing diagnostic-code helper is reused from `src/core/Aster.Core/Models/Policies/ResourcePolicyApplicationSummaries.cs`

**Checkpoint**: Summary models and shared helpers are available for user stories.

---

## Phase 3: User Story 1 - Summarize Policy Preview Candidates (Priority: P1) MVP

**Goal**: Summarize preview candidates with deterministic candidate, resource, target, outcome, and kind counts.

**Independent Test**: Manually construct a preview with mixed outcomes/kinds and verify all candidate summary fields.

### Tests for User Story 1

- [ ] T005 [P] [US1] Add mixed-candidate preview summary test in `test/Aster.Tests/Policies/PolicyPreviewSummaryTests.cs`

### Implementation for User Story 1

- [ ] T006 [US1] Implement candidate, resource, target, outcome, and kind counts in `src/core/Aster.Core/Models/Policies/ResourcePolicyPreviewSummaries.cs`

**Checkpoint**: User Story 1 is independently testable with focused summary tests.

---

## Phase 4: User Story 2 - Summarize Preview Diagnostics (Priority: P2)

**Goal**: Summarize preview diagnostics deterministically.

**Independent Test**: Manually construct a preview with repeated diagnostics, blank codes, and no candidates and verify diagnostic fields.

### Tests for User Story 2

- [ ] T007 [P] [US2] Add diagnostic summary test in `test/Aster.Tests/Policies/PolicyPreviewSummaryTests.cs`

### Implementation for User Story 2

- [ ] T008 [US2] Implement diagnostic count and diagnostic boolean fields in `src/core/Aster.Core/Models/Policies/ResourcePolicyPreviewSummaries.cs`

**Checkpoint**: User Story 2 is independently testable with focused summary tests.

---

## Phase 5: User Story 3 - Keep Policy Preview Summaries Pure (Priority: P3)

**Goal**: Verify summaries do not require services, providers, storage, or policy evaluation.

**Independent Test**: Construct preview objects directly and summarize them without building a service provider.

### Tests for User Story 3

- [ ] T009 [P] [US3] Add null-input and null-collection tests in `test/Aster.Tests/Policies/PolicyPreviewSummaryTests.cs`
- [ ] T010 [P] [US3] Add manual-construction purity coverage in `test/Aster.Tests/Policies/PolicyPreviewSummaryTests.cs`

### Implementation for User Story 3

- [ ] T011 [US3] Ensure preview summary helpers perform no service/provider/storage access in `src/core/Aster.Core/Models/Policies/ResourcePolicyPreviewSummaries.cs`

**Checkpoint**: All user stories are independently covered.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, roadmap, and validation.

- [ ] T012 [P] Update `docs/ExecutionRoadmap.md` to mark 026 landed and make 027 active
- [ ] T013 [P] Update `AGENTS.md` active technology and recent-change context for 027
- [ ] T014 Run focused tests: `dotnet test Aster.sln --filter "FullyQualifiedName~PolicyPreviewSummaryTests"`
- [ ] T015 Run full tests: `dotnet test Aster.sln`
- [ ] T016 Run build: `dotnet build Aster.sln /m:1`
- [ ] T017 Run whitespace validation: `git diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational.
- **User Story 2 (Phase 4)**: Depends on Foundational.
- **User Story 3 (Phase 5)**: Depends on User Story 1 and User Story 2 helper shape.
- **Polish (Phase 6)**: Depends on all selected user stories.

### User Story Dependencies

- **User Story 1 (P1)**: MVP and independently testable after foundational records exist.
- **User Story 2 (P2)**: Independently testable after foundational records exist.
- **User Story 3 (P3)**: Verifies purity and deterministic edge-case behavior across preview summaries.

### Parallel Opportunities

- T003 and T004 can be assessed independently.
- T005, T007, T009, and T010 are conceptually parallel but edit the same test file, so they should be applied carefully.
- T012 and T013 can be updated independently once implementation is complete.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Implement User Story 1 candidate summary and focused tests.
3. Run focused tests.

### Incremental Delivery

1. Add User Story 2 diagnostic summary.
2. Add User Story 3 purity and deterministic edge-case coverage.
3. Run full validation and update roadmap/agent context.

## Notes

- Keep implementation in records/extensions only.
- Do not add services, interfaces, provider registrations, storage, or dependencies.
- Mark each task complete as it is implemented.
