# Tasks: Provider Conformance Tests

**Input**: Design documents from `specs/006-provider-conformance-tests/`
**Prerequisites**: spec.md, plan.md, research.md, data-model.md, quickstart.md

## Phase 1: Setup

- [X] T001 Create `specs/006-provider-conformance-tests/spec.md`
- [X] T002 Create specification checklist in `specs/006-provider-conformance-tests/checklists/requirements.md`
- [X] T003 Create planning artifacts for provider conformance tests

## Phase 2: Tests And Harness

- [X] T004 Add shared provider conformance subject, query case, and failure models in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`
- [X] T005 Add capability-driven supported query checks in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`
- [X] T006 Add unsupported validation and execution checks in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`
- [X] T007 Add built-in in-memory and SQLite JSON provider conformance coverage in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`
- [X] T008 Add minimal custom-provider conformance coverage in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`
- [X] T009 Add intentionally broken custom-provider fixtures for diagnostic coverage in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`

## Phase 3: Documentation And Verification

- [X] T010 Document provider conformance guidance in `wiki/Querying.md`
- [X] T011 Run focused provider conformance tests
- [X] T012 Run `dotnet test Aster.sln`
- [X] T013 Run `dotnet build Aster.sln`
- [X] T014 Run `git diff --check`
- [X] T015 Update agent context with `.specify/scripts/bash/update-agent-context.sh copilot`
