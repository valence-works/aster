# Research: Schema Upgrade Summaries

## Decision: Use Pure Extension Helpers Over Services

**Decision**: Add `ToSummary()` extension helpers over schema status and schema upgrade result collections.

**Rationale**: Summary creation is deterministic in-memory aggregation over objects the host already has. A service would require registration, lifetime decisions, and indirection without current product value.

**Alternatives considered**:

- New summary service: rejected because it adds DI surface for pure object transformation.
- Provider-backed reporting: rejected because summaries should not read stores or depend on provider behavior.

## Decision: Keep Summary Models in Core Instance Models

**Decision**: Place schema summary records in `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs`.

**Rationale**: Existing schema status and upgrade models are instance lifecycle concepts, and prior summary slices place host-facing result summaries near the result types they summarize.

**Alternatives considered**:

- Separate reporting namespace: rejected because this would imply a broader reporting framework.
- Adding all records to existing schema files: rejected to avoid growing execution model files with aggregate helper code.

## Decision: Deterministic Counts and Stable Ordering

**Decision**: Group enum counts by enum value order, version counts by unknown bucket first then numeric version, and aspect-key counts by ordinal key order.

**Rationale**: Host UI snapshots, tests, and logs need repeatable output. Determinism also matches existing summary slices.

**Alternatives considered**:

- Preserve input order: rejected because aggregate output should not depend on incidental result order.
- String sorting enum names: rejected because enum numeric ordering is simpler and already used in similar slices.

## Decision: Explicit Unknown Source Version Bucket

**Decision**: Represent missing `SourceDefinitionVersion` values with an explicit `IsUnknown` flag in version-count rows.

**Rationale**: Unknown lineage is meaningful schema-upgrade information and should not be confused with version `0` or omitted from totals.

**Alternatives considered**:

- Omit unknown source versions: rejected because processed results would not reconcile cleanly.
- Use sentinel version `0`: rejected because it makes the contract less explicit.

## Decision: Ignore Blank Carried-Forward Aspect Keys in Key Counts

**Decision**: Ignore null/blank carried-forward aspect keys for per-key counts.

**Rationale**: Current production result creation returns concrete keys, but the summary helper should be robust over manually created result objects. Blank keys should not create confusing UI buckets.

**Alternatives considered**:

- Throw for blank keys: rejected because summaries should tolerate malformed aggregate inputs where possible.
- Count blank keys under an unknown bucket: rejected because aspect keys are identifiers and a blank bucket is not actionable.

## Decision: No Execution or Error Summary Expansion

**Decision**: Do not introduce failed-upgrade summaries because failed upgrades currently surface as exceptions rather than `ResourceSchemaUpgradeResult` objects.

**Rationale**: The slice summarizes existing result objects only. Capturing failed operations would require a new execution wrapper or result contract and would broaden scope.

**Alternatives considered**:

- Add failure result objects: rejected as a behavioral/API change outside this reporting slice.
- Add audit/error persistence: rejected as operationally broader than current requirements.
