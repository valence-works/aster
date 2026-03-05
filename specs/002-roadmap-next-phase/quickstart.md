# Quickstart — Persistence & Querying Essentials (Phase 2)

## Goal
Validate that the SQLite + JSON provider satisfies lifecycle durability and query behavior requirements from the Phase 2 spec.

## Prerequisites
- .NET SDK compatible with solution targets (`net8.0`, `net9.0`, `net10.0`).
- Local workspace at repository root.
- Feature branch `002-roadmap-next-phase`.

## 1) Build and Test Baseline
1. Restore and build solution.
2. Run existing test suite to confirm baseline before provider implementation.

Expected:
- Existing Phase 1 tests pass.

## 2) Enable SQLite Provider
1. Add provider project and wire DI registration in host/sample composition.
2. Configure connection string for local SQLite file.

Expected:
- Application starts with provider selected.

## 3) Validate User Story 1 (Durable Resource Lifecycle)
1. Register one or more resource definitions.
2. Create resource V1 and append multiple updates (V2+).
3. Activate one version in a channel; optionally activate another in a different channel.
4. Restart host/process.
5. Retrieve latest, historical, and active versions.

Expected:
- Version history persists unchanged.
- Activation state persists unchanged.
- No in-place historical mutation observed.

## 4) Validate User Story 2 (Persistent Querying)
1. Seed mixed resource data including missing values on at least one sortable field.
2. Execute metadata and aspect-value queries with `Equals`, `Contains`, and `Range`.
3. Execute sorted/paged queries.

Expected:
- Filter results are correct.
- Sorting is deterministic.
- Records with missing sort values are included and appear last.

## 5) Validate Non-Functional Criteria
1. Seed/prepare fixed dataset of 100k resource versions.
2. Run standard persisted query suite and measure completion time.
3. Run intentional concurrent update tests.

Expected:
- At least 95% of standard queries complete in under 2 seconds.
- Concurrency conflicts are raised for conflicting updates.
- No corrupted version history is observed.

## 6) Completion Checklist
- FR-001 through FR-011 covered by tests or executable validation scenarios.
- SC-001 through SC-004 evidence captured (test output and measurement notes).
- Provider remains behind core abstractions with no provider-specific API leakage.
