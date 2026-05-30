# Research: Policy Application Summaries

## Summary Shape

Decision: Add explicit summary records for marker-based policy application results and pruning application results.

Rationale: The two result families have different status vocabularies and affected-target semantics. Explicit records keep host-facing names clear without forcing one generic status enum.

Alternatives considered:

- One generic summary type: rejected because it would hide domain-specific names such as applied versus pruned.
- Add count properties directly to existing results only: rejected because existing results already have basic counts and need a richer reporting surface with diagnostics and affected target counts.

## Invocation Model

Decision: Generate summaries through pure helpers over existing result objects.

Rationale: Hosts already receive result objects from application services. A pure transformation keeps reporting deterministic and requires no DI registration or provider setup.

Alternatives considered:

- Add `IResourcePolicySummaryService`: rejected because it introduces DI and lifecycle surface without current need.
- Add summaries to application services automatically: rejected because it changes execution paths and makes summary generation less explicit.

## Diagnostic Code Counts

Decision: Use a shared `ResourcePolicyDiagnosticCodeCount` record ordered by diagnostic code using ordinal comparison, ignoring null/blank codes.

Rationale: Hosts need stable grouping for UI and logs. Ignoring blank codes avoids creating misleading buckets for malformed diagnostics.

Alternatives considered:

- Preserve first-seen diagnostic order: rejected because input ordering can vary by candidate order and is less stable for UI snapshots.
- Include blank diagnostic codes: rejected because stable policy diagnostics are expected to have meaningful codes.

## Completion Semantics

Decision: `HasFailures` is true only when candidates failed. `IsFullySuccessful` is true only when every candidate completed through a successful terminal status and no candidates were skipped or failed.

Rationale: Skipped candidates are not failures, but a host reporting "fully successful" should not include skipped duplicate/no-write results as full completion.

Alternatives considered:

- Treat skipped as successful: rejected because duplicate/skipped candidates did not perform the requested write.
- Treat skipped as failure: rejected because existing result semantics distinguish deterministic skips from failures.

## Affected Target Counts

Decision: Count distinct affected resources for marker-based application successes and distinct resource/version pairs for pruning successes.

Rationale: Marker-based application affects a resource lifecycle marker, while pruning affects a specific resource version. Counting only successful statuses avoids overstating impact.

Alternatives considered:

- Count all submitted candidates: rejected because total candidate count already exists.
- Count failed/skipped targets as affected: rejected because they did not change state.
