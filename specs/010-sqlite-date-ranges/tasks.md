# Tasks: SQLite Date-Like Facet Ranges

**Input**: Design documents from `specs/010-sqlite-date-ranges/`

## Implementation

- [X] T001 Update SQLite facet range capabilities to include date-like shapes.
- [X] T002 Tighten range validation for mixed numeric/date-like bounds if needed.
- [X] T003 Implement SQLite date-like facet range translation without changing numeric range behavior.
- [X] T004 Add structured execution failures for invalid date-like query bounds when validation is bypassed.
- [X] T005 Update provider conformance supported/unsupported date-like range cases.
- [X] T006 Add SQLite execution tests for inclusive, exclusive, one-sided, missing, null, malformed, and non-string stored values.
- [X] T007 Update capability, validator, and discovery tests.
- [X] T008 Update README/wiki/provider docs and roadmap status.

## Verification

- [X] T009 Run focused SQLite/query capability tests.
- [X] T010 Run `dotnet test Aster.sln`.
- [X] T011 Run `dotnet build Aster.sln`.
- [X] T012 Run `git diff --check`.
