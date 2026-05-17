# Research: Provider Authoring Ergonomics

## Decision 1: Add a small explicit DI helper instead of a provider registry

**Decision**: Add a provider-neutral `IServiceCollection` extension that registers a custom query service and matching capability provider together.

**Rationale**: Custom provider authors currently have to remember several related registrations and registration order. A small helper removes that repeated setup while keeping behavior explicit in host code.

**Alternatives considered**:

- Provider registry. Rejected because the feature needs registration ergonomics, not a new provider framework.
- Runtime scanning/discovery. Rejected because it violates explicitness and can hide provider selection.
- Docs/tests only. Rejected because the user selected a DI affordance and the current registration pattern is easy to misorder.

## Decision 2: Keep key mismatch detection in validation

**Decision**: The registration helper does not compare provider keys at registration time. Validation remains responsible for fail-closed behavior when the active provider key has no matching capability declaration.

**Rationale**: DI registration should stay simple and not instantiate services early or add lifecycle rules. Validation already has the active provider and capability declarations at runtime.

**Alternatives considered**:

- Instantiate services during registration to compare keys. Rejected because registration should not create singleton instances or require provider constructors to be side-effect-free.
- Add an options validator. Rejected as extra infrastructure for a small SDK affordance.

## Decision 3: Improve capabilities-not-declared diagnostics

**Decision**: Keep the stable failure code `capabilities-not-declared`, but include the active provider key when available and explain that a matching capability declaration must be registered.

**Rationale**: The current failure is safe but generic. Provider authors need fast feedback about what key is missing or mismatched.

**Alternatives considered**:

- Add a new failure code for mismatched keys. Rejected because both missing and mismatched declarations mean the active provider capabilities are not declared.
- Throw during validation construction. Rejected because preflight validation should remain non-throwing.

## Decision 4: Manual registration remains supported

**Decision**: The helper is the recommended path, but manual `IResourceQueryService` and `IResourceQueryCapabilitiesProvider` registration remains valid for advanced cases.

**Rationale**: Existing hosts and providers may already use manual registrations, decorators, or tests that require direct control.

**Alternatives considered**:

- Require all custom providers to use the helper. Rejected as unnecessarily restrictive.
- Change built-in providers to depend on a registry. Rejected as broad and speculative.

## Decision 5: No new dependencies or storage changes

**Decision**: Implement with existing DI abstractions, tests, and documentation only.

**Rationale**: Provider authoring ergonomics do not require new packages or operational infrastructure.

**Alternatives considered**:

- Add validation packages or source generators. Rejected as disproportionate and contrary to simplicity.
