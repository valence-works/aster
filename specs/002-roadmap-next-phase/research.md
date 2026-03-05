# Phase 0 Research — Persistence & Querying Essentials (Phase 2)

## Decision 1: Reference Backend
- Decision: Use Sqlite + JSON as the first production-grade persistence provider.
- Rationale: Matches clarified requirement, minimizes operational overhead, supports deterministic local/integration testing, and is fast to validate against 100k-version dataset.
- Alternatives considered:
  - PostgreSQL + JSONB: stronger scale path but higher setup and CI complexity for first provider.
  - MongoDB: document-native but larger operational and dependency surface for initial phase.

## Decision 2: Persistence Shape
- Decision: Use relational keys for identity/version constraints with JSON payload columns for aspects and definition snapshots.
- Rationale: Preserves append-only invariants and efficient version/channel lookups while retaining document-like flexibility.
- Alternatives considered:
  - Fully normalized relational model for facets/aspects: rigid schema and higher migration churn.
  - Single opaque JSON blob per resource without relational keys: weak integrity checks for version ordering and activation joins.

## Decision 3: Concurrency Strategy
- Decision: Enforce optimistic concurrency on update/activate operations using latest-version checks inside a transaction.
- Rationale: Aligns with constitution principle II and spec clarification; prevents silent overwrite while preserving append-only history.
- Alternatives considered:
  - Last-write-wins: violates immutable history safety expectations.
  - Automatic retries in provider: obscures conflicts and can create hidden user-facing race behavior.

## Decision 4: Activation Policy Representation
- Decision: Persist channel activation state separately with explicit channel policy (`single-active` or `multi-active`).
- Rationale: Matches configurable per-channel requirement and keeps activation independent from payload content.
- Alternatives considered:
  - Embed activation flags in each resource version row: difficult to enforce channel policy atomically.
  - Global activation mode only: fails clarified requirement for per-channel configurability.

## Decision 5: Query Execution Boundary
- Decision: Keep `ResourceQuery` as public AST contract and translate to parameterized Sqlite SQL with a constrained operator whitelist (`Equals`, `Contains`, `Range`) for Phase 2.
- Rationale: Maintains provider-agnostic API and safe evaluation guarantees while enabling persisted filtering/sorting/paging.
- Alternatives considered:
  - Expose raw SQL filters: violates portable query model and increases injection risk.
  - Pull-all-then-filter in memory: does not meet performance and scale goals for 100k versions.

## Decision 6: Missing Sort Value Semantics
- Decision: Include all matched records when sorting; records missing the sort field are always ordered last.
- Rationale: Matches explicit clarification and provides deterministic, user-friendly result ordering.
- Alternatives considered:
  - Exclude missing-sort records: loses data unexpectedly.
  - Fail query when values missing: too strict for operational filtering.

## Decision 7: Testing Strategy for Success Criteria
- Decision: Add persistence-focused unit/integration tests in `test/Aster.Tests/Persistence` plus restart-durability and query-correctness scenarios over a fixed 100k-version dataset.
- Rationale: Directly maps to SC-001 through SC-005 and keeps confidence local to provider behavior.
- Alternatives considered:
  - Rely on only existing in-memory tests: insufficient evidence for durable persistence behavior.
  - Performance-only benchmarking without correctness suite: risks fast but incorrect behavior.
