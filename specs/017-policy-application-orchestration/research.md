# Research: Policy Application Orchestration

## Decision: Add A Small Policy Application Service

Decision: Introduce a host-facing policy application service that accepts explicit application candidates and returns per-candidate results.

Rationale: Preview and direct marker writes solve different problems. Preview identifies candidate outcomes without mutation; direct marker writes apply a marker to one resource without policy context. Application orchestration is the missing host workflow between those two surfaces, and a small service keeps the behavior explicit and testable.

Alternatives considered:

- Extend policy preview to optionally apply outcomes: rejected because preview must remain non-mutating.
- Ask hosts to loop over lifecycle marker writes manually: rejected because it would duplicate stale-candidate, policy-mismatch, pruning-preview-only, and per-candidate reporting rules in every host.
- Add a policy engine or executor framework: rejected as broader than current requirements.

## Decision: Use Explicit Candidate Inputs Derived From Preview Output

Decision: Application requests contain selected candidates with policy ID, outcome, resource ID, optional resource version, and optional marker metadata.

Rationale: Hosts need to apply subsets of preview results. Copying the relevant preview candidate identity into an application candidate preserves explicit intent and avoids hidden re-evaluation of a policy scope.

Alternatives considered:

- Accept a whole preview result: rejected because hosts need subset application.
- Accept policy IDs and re-run evaluation internally: rejected because it hides scope and can apply outcomes the host did not select.
- Accept only resource IDs and marker state: rejected because policy declaration mismatch and preview-only pruning diagnostics require policy context.

## Decision: Partial Success With One Result Per Candidate

Decision: Application returns one result per input candidate and allows valid candidates to apply even when other candidates fail, except for same-resource lifecycle conflicts where every conflicting candidate for that resource fails.

Rationale: Administrative workflows need precise reporting and retry behavior. Per-candidate results are more useful than an all-or-nothing batch and align with the existing diagnostic style.

Alternatives considered:

- All-or-nothing batch: rejected because one stale or unsupported candidate should not block unrelated valid resources.
- Single aggregate status: rejected because it does not support operator reporting or targeted retries.

## Decision: Preflight Same-Resource Conflicting Lifecycle Outcomes

Decision: If one request includes archive and soft-delete candidates for the same resource, all conflicting lifecycle candidates for that resource fail before either marker is applied.

Rationale: Request-order-dependent behavior would be surprising and hard to retry. Preflight keeps outcomes deterministic and avoids applying one state simply because it appeared first.

Alternatives considered:

- First successful marker wins: rejected because it depends on input ordering.
- Fail the entire batch: rejected because unrelated resources should still be able to apply.

## Decision: Fail Stale Version Candidates

Decision: When a candidate includes a resource version, application fails it if that version is no longer latest for the resource in the effective tenant.

Rationale: Policy previews are point-in-time reports. Applying a stale candidate after a newer version exists can mark a resource based on outdated evidence.

Alternatives considered:

- Apply to the logical resource regardless of version drift: rejected because it can apply old intent to changed data.
- Apply with a warning: rejected because warning-only behavior still mutates state based on stale evidence.

## Decision: Validate Current Policy Declaration Compatibility

Decision: The referenced policy declaration must still exist on the current resource definition and its outcome must match the submitted lifecycle outcome.

Rationale: Hosts should not apply old preview data after policy intent has changed or disappeared. The service does not need to re-run full policy matching; it only confirms the current declaration still authorizes the submitted outcome shape.

Alternatives considered:

- Apply based only on resource and outcome: rejected because policy identity would become informational only.
- Allow missing policies but fail mismatched policies: rejected because deleted policy intent should not continue to drive application.

## Decision: Reuse Existing Lifecycle Marker Store Semantics

Decision: Supported application outcomes use the existing lifecycle marker store for marker reads and persistence after the policy application service performs target existence, idempotency, and conflict preflight.

Rationale: Policy application already performs candidate-bounded target reads, marker prefetch, current policy validation, duplicate handling, and same-resource conflict preflight. Writing through the marker store avoids repeating target reads in the marker service while still using the same persisted marker model.

Alternatives considered:

- Delegate each write through the lifecycle marker service: rejected because it would re-read latest resource versions per candidate after policy application has already validated candidate-bounded target existence.
- Add provider-specific application stores: rejected because no new storage is required.

## Decision: No New Lifecycle Hook Behavior

Decision: Policy application does not add lifecycle hook coverage in this slice.

Rationale: Existing hook contexts cover existing lifecycle operations. Adding policy application hook contexts would broaden public contracts before a demonstrated host need.

Alternatives considered:

- Invoke hooks around each marker write: rejected because direct marker writes do not currently define a hook pipeline.
- Add new policy application hooks: rejected as unnecessary infrastructure for this slice.

## Decision: Preserve Provider And Operational Simplicity

Decision: No provider registry, runtime scanning, scheduler, public SQL, public `IQueryable<Resource>`, storage migration, or destructive pruning write is added.

Rationale: Application orchestration is core SDK behavior over existing abstractions. Keeping it provider-agnostic and host-invoked preserves the product direction from policy foundations.

Alternatives considered:

- Provider-specific executors: rejected because application rules are not provider-specific.
- Background policy runner: rejected because scheduling and automatic execution are explicitly out of scope.
