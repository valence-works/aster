# Tasks: Policy Application Orchestration

**Input**: Design documents from `/specs/017-policy-application-orchestration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
**Tests**: Required by the feature specification independent tests and acceptance scenarios.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently after the shared contract/model foundation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and does not depend on incomplete tasks
- **[Story]**: Which user story the task belongs to
- All tasks include exact repository paths

## Phase 1: Setup

**Purpose**: Confirm the feature branch and planning artifacts are ready for implementation.

- [X] T001 Confirm feature context and task inputs in `/Users/sipke/Projects/ValenceWorks/aster/specs/017-policy-application-orchestration/spec.md`, `/Users/sipke/Projects/ValenceWorks/aster/specs/017-policy-application-orchestration/plan.md`, and `/Users/sipke/Projects/ValenceWorks/aster/specs/017-policy-application-orchestration/contracts/policy-application-orchestration.md`
- [X] T002 Restore and baseline the solution with `dotnet restore /Users/sipke/Projects/ValenceWorks/aster/Aster.sln`

---

## Phase 2: Foundational

**Purpose**: Add the public contract, request/result models, and registration surface used by every story.

**Critical**: No user story work can be completed until this phase is done.

- [X] T003 [P] Add `IResourcePolicyApplicationService` public contract with `ValueTask<ResourcePolicyApplicationResult> ApplyAsync(ResourcePolicyApplicationRequest request, CancellationToken cancellationToken = default)` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Abstractions/IResourcePolicyApplicationService.cs`
- [X] T004 [P] Add policy application request, candidate, result, candidate-status, and candidate-result models in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Policies/ResourcePolicyApplication.cs`
- [X] T005 Extend stable diagnostic constants for policy application failures in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Policies/ResourcePolicyResults.cs`
- [X] T006 Add `ResourcePolicyApplicationService` skeleton with constructor dependencies in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T007 Register `IResourcePolicyApplicationService` in `AddAsterCore()` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`

**Checkpoint**: Foundation ready; user story implementation can now begin.

---

## Phase 3: User Story 1 - Apply Previewed Lifecycle Outcomes (Priority: P1)

**Goal**: Hosts can explicitly apply selected archive and soft-delete preview candidates without mutating resource versions or activation state.

**Independent Test**: Create archive and soft-delete preview candidates, apply a selected subset, and verify only selected resources receive lifecycle markers while resource versions and activation state remain unchanged.

### Tests for User Story 1

- [X] T008 [P] [US1] Add policy application tests for archive, soft-delete, subset application, and no resource-version mutation in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationServiceTests.cs`
- [X] T009 [P] [US1] Add activation-state regression coverage for policy application in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationActivationTests.cs`

### Implementation for User Story 1

- [X] T010 [US1] Implement tenant resolution, empty request handling, and input-order result creation in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T011 [US1] Map archive and soft-delete policy outcomes to lifecycle marker writes through `IResourceLifecycleMarkerService` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T012 [US1] Preserve resource versions and activation state by avoiding resource writer and activation dependencies in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T013 [US1] Run focused P1 tests with `dotnet test /Users/sipke/Projects/ValenceWorks/aster/Aster.sln --filter PolicyApplication`

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Report Per-Candidate Application Results (Priority: P2)

**Goal**: Hosts receive deterministic per-candidate statuses, counts, and stable diagnostics for applied, already-satisfied, skipped, and failed candidates.

**Independent Test**: Submit a mixed request with valid candidates, already-marked resources, missing targets, stale versions, mismatched policy declarations, and unsupported pruning candidates; verify every input receives one stable result.

### Tests for User Story 2

- [X] T014 [P] [US2] Add tests for already-satisfied retries, duplicate same-outcome candidates, aggregate counts, and one result per input in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationResultTests.cs`
- [X] T015 [P] [US2] Add tests for invalid candidate shape, missing resource, unsupported outcome, pruning preview-only, stale version, missing policy, mismatched policy, and conflicting same-resource outcomes in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationDiagnosticsTests.cs`

### Implementation for User Story 2

- [X] T016 [US2] Add candidate shape validation and invalid-candidate diagnostics in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T017 [US2] Add unsupported outcome and `policy-pruning-preview-only` handling in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T018 [US2] Add duplicate same-outcome handling and deterministic `Skipped` or `AlreadySatisfied` result behavior in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T019 [US2] Add latest-version lookup and stale-candidate diagnostics through `IResourceVersionReader` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T020 [US2] Add current policy declaration existence and outcome-match validation through `IResourceDefinitionStore` in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T021 [US2] Add same-resource archive/soft-delete conflict preflight before marker writes in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T022 [US2] Map lifecycle marker service conflicts and target-not-found results into policy application candidate results in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T023 [US2] Compute aggregate applied, already-satisfied, skipped, and failed counts from candidate results in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Policies/ResourcePolicyApplication.cs`
- [X] T024 [US2] Run focused P2 tests with `dotnet test /Users/sipke/Projects/ValenceWorks/aster/Aster.sln --filter PolicyApplication`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Bound Application To Explicit Host Intent (Priority: P3)

**Goal**: Application remains bounded by explicit request data, tenant scope, and selected outcomes without becoming an automatic scheduler, policy engine, or provider-specific executor.

**Independent Test**: Submit tenant-scoped requests and unsupported policy kinds, verify no cross-tenant marker writes, no automatic execution, no pruning writes, and no lifecycle hook behavior changes.

### Tests for User Story 3

- [X] T025 [P] [US3] Add tenant boundary tests proving outside-tenant resources fail as target-not-found without marker writes in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/TenantPolicyApplicationTests.cs`
- [X] T026 [P] [US3] Add no-automatic-execution and lifecycle-hook non-goal regression tests in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationCompatibilityTests.cs`
- [X] T027 [P] [US3] Add SQLite JSON registration compatibility tests for policy application in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/SqliteJson/SqliteJsonPolicyApplicationTests.cs`

### Implementation for User Story 3

- [X] T028 [US3] Ensure all resource, definition, and marker operations use the effective tenant scope in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyApplicationService.cs`
- [X] T029 [US3] Keep policy application out of preview, resource write, activation, query, portability, and lifecycle hook flows in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`
- [X] T030 [US3] Verify SQLite JSON behavior uses existing core service registration without provider-specific application executors in `/Users/sipke/Projects/ValenceWorks/aster/src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [X] T031 [US3] Run tenant and SQLite focused tests with `dotnet test /Users/sipke/Projects/ValenceWorks/aster/Aster.sln --filter "PolicyApplication|SqliteJsonPolicyApplication|TenantPolicyApplication"`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, quickstart validation, and final repository verification.

- [X] T032 [P] Update policy application quickstart examples if public API names changed during implementation in `/Users/sipke/Projects/ValenceWorks/aster/specs/017-policy-application-orchestration/quickstart.md`
- [X] T033 [P] Update public contract documentation if implemented behavior differs from planned names without changing requirements in `/Users/sipke/Projects/ValenceWorks/aster/specs/017-policy-application-orchestration/contracts/policy-application-orchestration.md`
- [X] T034 Re-run the constitution check and remove unnecessary abstractions or dependencies in `/Users/sipke/Projects/ValenceWorks/aster/specs/017-policy-application-orchestration/plan.md`
- [X] T035 Run full verification with `dotnet test /Users/sipke/Projects/ValenceWorks/aster/Aster.sln`
- [X] T036 Run final build with `dotnet build /Users/sipke/Projects/ValenceWorks/aster/Aster.sln /m:1`
- [X] T037 Run whitespace validation with `git -C /Users/sipke/Projects/ValenceWorks/aster diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational; MVP delivery.
- **User Story 2 (Phase 4)**: Depends on Foundational and builds on the service behavior introduced for User Story 1.
- **User Story 3 (Phase 5)**: Depends on Foundational and can proceed after User Story 1 core application behavior exists.
- **Polish (Phase 6)**: Depends on all desired user stories.

### User Story Dependencies

- **User Story 1 (P1)**: First implementation target after Foundational.
- **User Story 2 (P2)**: Can start after Foundational, but its implementation tasks are simpler after User Story 1 writes the main service path.
- **User Story 3 (P3)**: Can start after Foundational, but its tests rely on the application service contract and registration.

### Within Each User Story

- Write tests first and verify they fail before implementation.
- Add or extend models before service behavior that consumes them.
- Add service behavior before registration or provider compatibility assertions.
- Run focused tests at each checkpoint before moving to the next story.

### Parallel Opportunities

- T003 and T004 can run in parallel.
- T008 and T009 can run in parallel.
- T014 and T015 can run in parallel.
- T025, T026, and T027 can run in parallel.
- T032 and T033 can run in parallel after implementation behavior is stable.

---

## Parallel Example: User Story 1

```text
Task: "T008 [P] [US1] Add policy application tests for archive, soft-delete, subset application, and no resource-version mutation in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationServiceTests.cs"
Task: "T009 [P] [US1] Add activation-state regression coverage for policy application in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationActivationTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T014 [P] [US2] Add tests for already-satisfied retries, duplicate same-outcome candidates, aggregate counts, and one result per input in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationResultTests.cs"
Task: "T015 [P] [US2] Add tests for invalid candidate shape, missing resource, unsupported outcome, pruning preview-only, stale version, missing policy, mismatched policy, and conflicting same-resource outcomes in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationDiagnosticsTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T025 [P] [US3] Add tenant boundary tests for policy application in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/TenantPolicyApplicationTests.cs"
Task: "T026 [P] [US3] Add no-automatic-execution and lifecycle-hook non-goal regression tests in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/PolicyApplicationCompatibilityTests.cs"
Task: "T027 [P] [US3] Add SQLite JSON registration compatibility tests for policy application in /Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/SqliteJson/SqliteJsonPolicyApplicationTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete User Story 1.
3. Validate archive and soft-delete marker application works for selected candidates only.
4. Stop and verify P1 behavior before adding broader diagnostics.

### Incremental Delivery

1. Foundation: public contract, models, service skeleton, DI registration.
2. P1: supported lifecycle outcome application.
3. P2: per-candidate reporting, diagnostics, idempotency, stale/policy/conflict validation.
4. P3: tenant boundaries, non-goals, provider compatibility.
5. Polish: docs and full solution verification.

### Implementation Notes

- Keep the service provider-agnostic and use existing abstractions only.
- Do not add a provider registry, runtime scanning, scheduler, public SQL, public `IQueryable<Resource>`, destructive pruning writes, or lifecycle hook behavior.
- Prefer direct validation helpers inside `ResourcePolicyApplicationService` unless duplication proves a small private helper is needed.
- Keep manual lifecycle marker writes and policy previews unchanged.
