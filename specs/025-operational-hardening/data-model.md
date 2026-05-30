# Data Model: Operational Hardening

## Restore Retry Scenario

Represents applying the same restore candidate more than once.

Fields:

- Resource identifier.
- Expected lifecycle marker state.
- Effective tenant.
- Application result statuses.
- Final lifecycle marker state.

Rules:

- First successful restore clears the marker.
- Retry observes already-restored behavior.
- Concurrent same-candidate calls leave the marker cleared and do not recreate marker state.

## Pruning Retry Scenario

Represents applying the same version-pruning candidate more than once.

Fields:

- Resource identifier.
- Target resource version.
- Effective tenant.
- Application result statuses.
- Remaining version list.

Rules:

- First successful pruning removes only the target version.
- Retry observes already-pruned behavior.
- Remaining versions are unchanged by retry.

## Historical Activation Retry Scenario

Represents activating the same existing resource version more than once.

Fields:

- Resource identifier.
- Target version.
- Channel.
- Multi-active setting.
- Active version list.
- Latest version.

Rules:

- Single-active retry leaves one active version.
- Multi-active retry leaves a unique ordered active version list.
- Latest version remains the highest saved version and is not rewritten by activation.

## State Transitions

No new state transitions are introduced. Tests exercise existing restore, pruning, and activation transitions.
