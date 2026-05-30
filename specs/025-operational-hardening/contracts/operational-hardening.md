# Contract: Operational Hardening

Operational hardening is delivered as regression coverage over existing public SDK behavior.

## Expected Behavior

Lifecycle restore:

- Applying the same valid restore candidate after the marker is cleared reports already-restored behavior.
- Concurrent same-candidate restore attempts leave the lifecycle marker cleared.

Policy pruning application:

- Retrying the same pruned target reports already-pruned behavior.
- Retry does not remove latest, active, or unrelated versions.
- SQLite persisted state preserves retry behavior after provider reopen.

Historical activation:

- Repeating single-active historical activation leaves exactly one active version for the channel.
- Repeating multi-active historical activation leaves active versions unique and ordered.
- Latest version remains separate from activation state.

## Non-Goals

This slice does not add:

- new public APIs;
- storage schema changes;
- provider registries or provider extensions;
- public SQL or public `IQueryable<Resource>`;
- runtime scanning or automatic discovery;
- schedulers, background jobs, benchmark infrastructure, audit persistence, or new dependencies.

## Validation Contract

The slice is complete when:

- focused operational hardening tests pass;
- full solution tests pass;
- build passes;
- whitespace check passes;
- roadmap marks `024-version-history-summaries` landed and `025-operational-hardening` active.
