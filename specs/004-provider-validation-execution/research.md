# Research: Provider Validation Execution Alignment

## Decision 1: Match providers and capabilities with an explicit provider key

**Decision**: Add an explicit provider key to the active query provider and to its capability declaration. Validation uses this key to select capabilities.

**Rationale**: Type-name matching is brittle and can validate against stale defaults when hosts replace `IResourceQueryService`. An explicit key is discoverable, testable, and consistent with the constitution's explicitness principle.

**Alternatives considered**:

- Last registered capability declaration. Rejected because default capabilities can remain registered after provider replacement.
- Concrete type-name matching. Rejected because it is convention-based and fails for wrappers, decorators, or custom provider naming.
- Require exactly one capability declaration. Rejected because hosts may register multiple providers during setup or testing.

## Decision 2: Introduce structured unsupported execution failures

**Decision**: Execution-time unsupported query failures expose stable code, feature category, path when available, and actionable message.

**Rationale**: Validation already produces structured failures. Execution must remain authoritative, but callers need consistent handling when validation is skipped or stale.

**Alternatives considered**:

- Message-only consistency. Rejected because tests and callers need stable categories that do not depend on message text.
- Throw full validation result. Rejected because execution may stop at the first provider-specific blocking failure and should not imply all failures were detected.
- Keep existing exception unchanged. Rejected because it cannot satisfy stable code/category requirements.

## Decision 3: Providers run shared validation before execution, then keep provider-specific checks

**Decision**: Provider query services run shared validation before translating/executing a query. Provider-specific translation and execution checks remain in place.

**Rationale**: Shared validation aligns common unsupported-shape feedback and reduces drift. Provider-specific checks still protect against capability declaration bugs and constraints only known during translation.

**Alternatives considered**:

- Rely only on shared validation. Rejected because execution must remain authoritative and provider-specific conditions can exist.
- Keep current provider checks only. Rejected because validation/execution consistency remains manual and drift-prone.
- Share lower-level helper functions without running validation. Rejected as more internal coupling with less user-visible consistency.

## Decision 4: Keep preflight validation non-throwing

**Decision**: Preflight validation continues to return `QueryValidationResult`; only execution raises unsupported query exceptions.

**Rationale**: Preflight failures are expected user feedback and should remain easy to render in applications. Execution failures are exceptional because the provider was asked to perform unsupported work.

**Alternatives considered**:

- Make validation throw. Rejected because it would regress current validation ergonomics.
- Make execution return validation results. Rejected because it would break existing query execution shape and mix successful result enumeration with failure reporting.

## Decision 5: No new dependencies or provider framework

**Decision**: Implement provider identity, validation reuse, and structured exceptions using existing platform and project dependencies.

**Rationale**: The current need is small and direct: identify providers, select matching capabilities, and produce consistent failures. A larger provider framework would be premature.

**Alternatives considered**:

- Add a provider registry package or options framework. Rejected because existing DI and small contracts are sufficient.
- Add a query planner. Rejected because this feature does not rewrite queries or negotiate provider capabilities.
