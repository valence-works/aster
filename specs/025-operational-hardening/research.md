# Research: Operational Hardening

## Focused Regression Tests Over Stress Infrastructure

Decision: Add bounded xUnit regression tests instead of benchmark, load, or stress-test infrastructure.

Rationale: The current need is confidence in recently added Phase 5 workflows. Focused tests are deterministic, cheap to run locally, and consistent with operational simplicity.

Alternatives considered:

- Add benchmark suite: rejected because it adds infrastructure and timing concerns without a measured performance requirement.
- Add randomized concurrency tests: rejected because nondeterminism would reduce reliability.
- Add background job simulation: rejected because schedulers and jobs are out of scope.

## Restore Retry And Concurrent Same-Candidate Coverage

Decision: Cover lifecycle restore retry plus one bounded concurrent same-candidate restore scenario.

Rationale: Restore is recovery-oriented and safe retry semantics matter operationally. The concurrent scenario verifies the existing clear-if-state-matches behavior remains safe when two callers race the same marker.

Alternatives considered:

- Cover every marker state and provider combination: rejected as too broad; existing tests cover marker state/provider basics.
- Add locking to the service preemptively: rejected unless tests expose a bug.

## Pruning Retry Coverage

Decision: Cover in-memory retry and SQLite persisted retry for the same pruning candidate.

Rationale: Pruning is destructive. Retrying the same target should report already-pruned behavior and must not delete additional versions after the original target is gone.

Alternatives considered:

- Add concurrent pruning tests: deferred because destructive concurrent writes can be provider timing-sensitive and need a separate design if broadened.
- Add audit records for pruning retries: rejected because persistence/audit is out of scope.

## Repeated Historical Activation Coverage

Decision: Cover repeated activation for single-active and multi-active channels.

Rationale: Hosts may retry activation calls after transient failures. Active version lists should remain unique and ordered, while latest version identity remains separate from activation state.

Alternatives considered:

- Add new idempotency result models: rejected because existing activation APIs are command-style and no product behavior change is requested.
- Add activation concurrency tests: deferred to a deeper concurrency slice if needed.

## Out-Of-Scope Boundaries

Decision: Do not add new APIs, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, schedulers, background jobs, benchmark infrastructure, or dependencies.

Rationale: This is a hardening slice. Any runtime change should be justified by a failing deterministic regression test.
