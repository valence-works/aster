# Research: Version History Summaries

## Pure Extension Helpers

Decision: Add `ToSummary` extension helpers over `ResourceVersionHistoryResult` and `ResourceVersionHistoryBatchResult`.

Rationale: Hosts already receive result objects from history services or can construct them in tests. Pure helpers avoid a service abstraction, DI registration, provider dependency, or hidden reads.

Alternatives considered:

- Add an `IResourceVersionHistorySummaryService`: rejected because no external collaborator or lifecycle is needed.
- Add summary calculation inside history services only: rejected because manually constructed results should be summarizable without services.
- Add provider-side summary queries: rejected because summaries are derived from already materialized results.

## Count Semantics

Decision: Summaries count existing `ResourceVersionSummary` fields: total, latest, draft, active, protected, possible candidate, and lifecycle states.

Rationale: These are stable values already computed by history inspection. Summaries should not introduce new product semantics or imply policy eligibility.

Alternatives considered:

- Add definitive prune eligibility counts: rejected because policy evaluation owns eligibility.
- Add time-window or age buckets: deferred because they require product decisions about retention windows.
- Add channel-specific aggregate maps: deferred until hosts demonstrate the need.

## Batch Aggregation

Decision: Batch summaries aggregate across histories and report selected resources, resources with versions, missing resources, and version-state totals.

Rationale: This gives host dashboards useful top-level numbers while preserving the explicit selected-resource shape from batch history.

Alternatives considered:

- Return one nested single summary per resource: rejected for this slice because callers can already call `ToSummary` per history; the batch summary should be aggregate.
- Exclude missing resources from selected counts: rejected because selected-resource count should reflect caller selection.

## Null Collection Handling

Decision: Null `Versions` and `Histories` collections are treated as empty; null result inputs fail fast.

Rationale: Result object collections normally default to empty, but manually constructed objects can set them to null. Treating collections as empty matches policy summary precedent and keeps helpers host-friendly.

Alternatives considered:

- Throw for null collections: rejected as unnecessary strictness for derived summaries.
- Silently accept null result input: rejected because extension helpers need a real source identity.

## Out-Of-Scope Boundaries

Decision: Do not add storage, services, providers, policy evaluation, audit persistence, public SQL, public `IQueryable<Resource>`, query planning, runtime scanning, automatic discovery, background jobs, or mutation behavior.

Rationale: The feature is a pure view over result objects. Broader reporting or audit features need separate product and storage decisions.
