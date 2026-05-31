# Research: Policy Preview Summaries

## Decision: Use Pure Extension Helpers

Use an extension method over `ResourcePolicyEvaluationPreview`.

**Rationale**: The feature only aggregates data already present on preview result objects. A service would add registration and lifetime surface without reading external state or coordinating collaborators.

**Alternatives considered**:

- Service-based summarization: rejected because there is no dependency to inject and no provider state to access.
- Computed properties on preview results only: rejected because summaries combine multiple grouped counts and the existing pattern uses explicit `ToSummary` helpers.

## Decision: Count Outcomes and Kinds by Enum Ordering

Group candidates by `ResourcePolicyOutcome` and `ResourcePolicyKind`, then order by enum value.

**Rationale**: Enum ordering is deterministic, stable for tests, and avoids host-specific sort rules.

**Alternatives considered**:

- Alphabetical display-name ordering: rejected because display names are not part of the model and would add presentation assumptions.
- Preserve first-seen order: rejected because summaries should be deterministic independent of candidate order for grouped count display.

## Decision: Count Distinct Resources and Version Targets Separately

Resource count uses nonblank `ResourceId`. Resource-version target count uses `(ResourceId, ResourceVersion)` when both are present.

**Rationale**: Policy previews can target logical resources or specific versions. Hosts need both counts without inferring version targeting from outcome names.

**Alternatives considered**:

- Count only resource IDs: rejected because version-pruning previews need a version-target count.
- Count every candidate as a target: rejected because repeated candidates would inflate operator-facing counts.

## Decision: Reuse Policy Diagnostic Count Model and Helper

Diagnostic code counts use the existing `ResourcePolicyDiagnosticCodeCount` model and `ResourcePolicyDiagnosticCodeCounter`.

**Rationale**: Preview diagnostics already use `ResourcePolicyDiagnostic`. Reusing the count model avoids duplicated concepts across policy operation summaries.

**Alternatives considered**:

- Add a preview-specific diagnostic count record: rejected as unnecessary duplication.
- Emit only a boolean: rejected because hosts need stable diagnostic breakdowns for troubleshooting.

## Decision: Null Collections Are Empty

Summary helpers fail fast for null preview result objects but treat null candidate and diagnostic collections as empty.

**Rationale**: This matches existing summary behavior and keeps manually constructed test/UI DTOs easy to summarize while still catching programming errors for null result inputs.

**Alternatives considered**:

- Throw for null candidate or diagnostic collections: rejected because result records are host-constructible and existing summary helpers already tolerate null collections.
