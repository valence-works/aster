# Tasks: Typed Query Authoring Ergonomics

**Input**: Design documents from `specs/008-typed-query-authoring/`

## Implementation

- [X] T001 Add typed facet sort helpers to `TypedFacetQuery<TValue>`.
- [X] T002 Add logical composition helpers to `TypedQuery`.
- [X] T003 Add argument validation for empty logical operands and invalid `Not` operands.
- [X] T004 Extend typed query helper tests for ascending/descending facet sorts.
- [X] T005 Extend typed query helper tests for overrides and selector rejection on sort helpers.
- [X] T006 Extend typed query helper tests for `And`, `Or`, and `Not` composition.
- [X] T007 Add validation/execution coverage for helper-generated filters and sorts against existing providers.
- [X] T008 Update core README and querying wiki examples.

## Verification

- [X] T009 Run focused typed query tests.
- [X] T010 Run `dotnet test Aster.sln`.
- [X] T011 Run `dotnet build Aster.sln`.
- [X] T012 Run `git diff --check`.
