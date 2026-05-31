# Research: Policy Validation Summaries

## Decision: Add a Focused Policy Validation Summary File

**Decision**: Add `ResourcePolicyValidationSummaries.cs` beside existing policy result and summary models.

**Rationale**: Policy validation has a distinct source result type and distinct grouping dimensions. A focused file keeps the public surface discoverable without making the existing application/preview summary files broader.

**Alternatives considered**:

- Extend `ResourcePolicyApplicationSummaries.cs`: rejected because validation summaries are not application results and would blur file ownership.
- Add computed properties to `ResourcePolicyValidationResult`: rejected because grouped counts are derived views and existing slices use explicit `ToSummary()` helpers.

## Decision: Reuse Existing Diagnostic Code Count Shape

**Decision**: Reuse `ResourcePolicyDiagnosticCodeCount` for diagnostic-code grouping and add new validation-specific count records for path, policy id, resource id, and resource version.

**Rationale**: The diagnostic-code count already exists as a public policy summary concept. Reusing it avoids duplicate public shapes with the same meaning. Other dimensions do not yet have matching public count records.

**Alternatives considered**:

- Add a generic string count record: rejected because existing public summary models use explicit domain names for readability.
- Duplicate a validation-specific diagnostic-code count: rejected because it would create redundant public API for the same concept.

## Decision: Keep Grouping Deterministic and Conservative

**Decision**: String counts use ordinal ordering and omit null/blank keys. Resource version counts include only diagnostics with a version and order numerically.

**Rationale**: Deterministic output supports stable tests, logs, dashboards, and host comparisons. Omitting blank string keys matches existing diagnostic-count behavior while preserving total diagnostic count accuracy.

**Alternatives considered**:

- Include blank keys as a special bucket: rejected because current summary patterns ignore blank codes and because hosts can still see total diagnostics.
- Preserve input order: rejected because grouped summaries should be stable regardless of diagnostic order.

## Decision: Treat Null Diagnostic Collections as Empty

**Decision**: Summary creation throws when the validation result is null but treats a null `Diagnostics` collection as empty.

**Rationale**: This follows existing summary helper behavior: the root result is required, while nested collections are defensive and safe to treat as empty for manually constructed test objects.

**Alternatives considered**:

- Throw on null nested diagnostics: rejected because existing summaries tolerate null nested collections and hosts may manually construct result objects.

## Decision: Validate Compatibility with Existing Policy Validation Tests

**Decision**: Add focused summary tests and run existing policy validation tests plus full solution validation.

**Rationale**: The slice must not change validation behavior. Existing validation tests are the strongest compatibility guard, while summary tests verify the new public SDK surface.

**Alternatives considered**:

- Only run new summary tests: rejected because the feature explicitly promises no validation behavior changes.
