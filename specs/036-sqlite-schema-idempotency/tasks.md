# Tasks: SQLite Schema Idempotency Hardening

**Input**: Design documents from `/specs/036-sqlite-schema-idempotency/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sqlite-schema-idempotency.md, quickstart.md

**Tests**: Required. This is a test-focused operational hardening slice.

## Phase 1: Setup

- [x] T001 Confirm branch `036-sqlite-schema-idempotency` is active
- [x] T002 Confirm Constitution Check gates still pass before implementation

## Phase 2: Foundational

- [x] T003 Review `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`
- [x] T004 Review existing SQLite tenant legacy tests in `test/Aster.Tests/SqliteJson/SqliteJsonTenantScopeTests.cs`
- [x] T005 Confirm no production changes are needed before tests expose a defect

## Phase 3: Repeated Initialization (P1)

- [x] T006 Add `test/Aster.Tests/SqliteJson/SqliteJsonSchemaIdempotencyTests.cs`
- [x] T007 Add repeated provider initialization test that persists and rereads definition/resource/activation/marker state
- [x] T008 Add tenant-aware primary-key metadata assertions after repeated initialization
- [x] T009 Run `dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonSchemaIdempotencyTests"`

## Phase 4: Legacy Upgrade Rerun (P2)

- [x] T010 Add legacy pre-tenant table setup in `SqliteJsonSchemaIdempotencyTests.cs`
- [x] T011 Add repeated legacy-upgrade initialization test that verifies default-tenant row counts are not duplicated
- [x] T012 Add bootstrap-table absence assertions
- [x] T013 Run `dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonSchemaIdempotencyTests"`

## Phase 5: No-Initialization Compatibility (P3)

- [x] T014 Add `InitializeSchema = false` identity/capability resolution test
- [x] T015 Verify no database file is created
- [x] T016 Run compatibility tests with `dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonTenantScopeTests|FullyQualifiedName~SqliteJsonResourceStoreTests"`

## Phase 6: Validation & Polish

- [x] T017 Run `dotnet test Aster.sln`
- [x] T018 Run `dotnet build Aster.sln /m:1`
- [x] T019 Run `git diff --check`
- [x] T020 Update `AGENTS.md` recent change entry from planning context to shipped implementation
- [x] T021 Mark all tasks complete

## Notes

- Keep production code unchanged unless tests expose a concrete defect.
- Do not introduce new APIs, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, schedulers, benchmark infrastructure, or dependencies.
