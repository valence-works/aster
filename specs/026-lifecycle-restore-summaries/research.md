# Research: Lifecycle Restore Summaries

## Decision: Use Pure Extension Helpers

Use extension methods over `ResourceLifecycleRestoreApplicationResult` and `ResourceLifecycleRestorePreviewResult`.

**Rationale**: The feature only aggregates data already present on result objects. A service would add registration and lifetime surface without reading external state or coordinating collaborators.

**Alternatives considered**:

- Service-based summarization: rejected because there is no dependency to inject and no provider state to access.
- Result object computed properties only: rejected because summaries combine multiple counts and diagnostic groupings, and the existing pattern uses explicit `ToSummary` helpers.

## Decision: Mirror Existing Policy Summary Concepts

Use summary records with total/status counts, `HasFailures`, `IsFullySuccessful`, distinct resource counts, and diagnostic code counts.

**Rationale**: Hosts already have a policy application/pruning summary pattern. Restore is another operation result with per-candidate statuses and diagnostics, so using the same conceptual shape reduces learning cost.

**Alternatives considered**:

- One generic operation summary type: rejected because status names and successful terminal statuses differ by operation and a generic type would obscure restore-specific semantics.
- Only expose count properties on existing results: rejected because affected resource counts and diagnostic code groupings would remain duplicated in hosts.

## Decision: Preview and Application Have Different Successful Terminal Sets

Preview `IsFullySuccessful` counts `Restorable` and `AlreadyRestored`; application `IsFullySuccessful` counts `Restored` and `AlreadyRestored`.

**Rationale**: Preview and application use the same candidate result type but represent different phases. Treating `Restorable` as application success or `Restored` as preview success would hide misuse.

**Alternatives considered**:

- Treat every non-failed status as successful: rejected because skipped duplicate candidates are deterministic but not successful operator outcomes.
- Use one shared helper for both result types: rejected because it would blur phase-specific semantics.

## Decision: Distinct Resource Counts Use Successful Candidate Resource Identifiers

Application affected resources count nonblank resource IDs from `Restored` and `AlreadyRestored` candidates. Preview candidate resources count nonblank resource IDs from `Restorable` and `AlreadyRestored` candidates.

**Rationale**: These are the resources a host can reasonably present as already satisfied or able to proceed. Failed, skipped, and blank candidates should not inflate success-oriented counts.

**Alternatives considered**:

- Count all candidate resource IDs: rejected because failures and duplicates would inflate operator-facing success counts.
- Count only write-producing restored candidates: rejected because idempotent already-restored candidates are also successful terminal outcomes for restore workflows.

## Decision: Reuse Policy Diagnostic Model

Diagnostic code counts aggregate existing `ResourcePolicyDiagnostic` values from candidate diagnostics.

**Rationale**: Lifecycle restore already reports stable diagnostics using the policy diagnostic model. Reusing it avoids a second diagnostic shape.

**Alternatives considered**:

- Add restore-specific diagnostic count record: rejected as unnecessary duplication.
- Emit only failed count: rejected because hosts need stable diagnostic breakdowns for troubleshooting.

## Decision: Null Collections Are Empty

Summary helpers fail fast for null result objects but treat null candidate and diagnostic collections as empty.

**Rationale**: This matches existing summary behavior and keeps manually constructed test/UI DTOs easy to summarize while still catching programming errors for null result inputs.

**Alternatives considered**:

- Throw for null candidate collections: rejected because result records are host-constructible and existing summary helpers already tolerate null collections.
