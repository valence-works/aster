# Research: Host Lifecycle Hooks

## Decision: Use Explicit DI-Registered Hook Services

**Decision**: Hosts register lifecycle hooks explicitly through dependency injection. The SDK invokes the registered hooks in deterministic service registration order for each lifecycle point.

**Rationale**: This matches the existing provider-authoring direction: explicit registration, no runtime scanning, no hidden discovery, and no provider registry framework. It is straightforward to test, debug, and delete later.

**Alternatives considered**:

- Attribute-based discovery: rejected because it requires runtime scanning and hides behavior from normal DI configuration.
- Assembly scanning: rejected by the constitution's explicitness and operational simplicity principles.
- Named provider registry: rejected because hooks are host integration points, not provider implementations.

## Decision: Centralize Invocation In A Small Hook Coordinator

**Decision**: Add a lifecycle hook coordinator that accepts lifecycle context, runs matching registered hooks in order, observes cancellation, and returns a structured outcome.

**Rationale**: A coordinator avoids duplicating ordering, cancellation, and failure mapping in `DefaultResourceManager`, `ResourceSchemaVersionService`, and `ResourcePortabilityService`. It is a small abstraction with a demonstrated need across multiple existing services.

**Alternatives considered**:

- Inline hook loops in each service: rejected because cancellation and failure behavior would likely drift across save, activation, and portability operations.
- Generic workflow pipeline: rejected as too broad for this slice and harder to remove.

## Decision: Model Contexts As Immutable Operation-Specific Records

**Decision**: Use operation-specific context records for save, activation, export, preview import, and write import rather than a mutable dictionary or generic bag.

**Rationale**: Immutable records make available data discoverable, preserve operation state, and prevent hooks from accidentally mutating the operation. Operation-specific contexts keep each lifecycle point readable and strongly typed.

**Alternatives considered**:

- Mutable context bag: rejected because it introduces implicit side effects and unclear ownership of changed values.
- Single generic context with optional properties: rejected because it makes lifecycle-specific behavior less discoverable and increases null-heavy code.

## Decision: Before Hooks Can Reject Before Mutation

**Decision**: Before hooks can return a structured rejection. Once a hook rejects, later hooks for that lifecycle point are not invoked and the underlying operation is not performed.

**Rationale**: Hosts need a predictable policy gate. Stopping at the first rejection keeps behavior deterministic and avoids conflicting failure messages from later hooks.

**Alternatives considered**:

- Aggregate all before-hook rejections: deferred because it adds complexity and requires all hooks to run even after a decisive rejection.
- Throw-only rejection: rejected because structured outcomes are easier to surface in portability diagnostics and tests.

## Decision: After Hooks Are Success Observers, Not Rollback Participants

**Decision**: After hooks run only after a successful underlying operation. If an after hook fails, the failure is visible to the caller, but the SDK does not claim the operation was rolled back.

**Rationale**: Existing storage operations do not provide cross-cutting transaction participation across arbitrary host hooks. Reporting post-commit failures honestly is simpler and avoids false rollback guarantees.

**Alternatives considered**:

- Transactional after hooks: rejected because it would couple hooks to provider transactions and require provider-specific behavior.
- Swallow after-hook failures: rejected because hosts need deterministic, inspectable outcomes.

## Decision: Keep Portability Failures Aligned With Existing Diagnostics

**Decision**: Portability hook rejections and hook failures should surface through portability result diagnostics where the operation already returns diagnostics. Lifecycle methods that currently throw structured exceptions can use a hook-specific structured exception.

**Rationale**: This keeps the public behavior consistent with existing operation result shapes. Portability already returns diagnostics; resource save and activation methods currently signal failures through exceptions.

**Alternatives considered**:

- Convert all lifecycle methods to result objects: rejected as a broad breaking API change.
- Use only exceptions everywhere: rejected for portability operations because diagnostics are already the established pattern.

## Decision: No New Dependencies Or Storage Changes

**Decision**: Implement hooks using platform and existing SDK capabilities only. Do not persist hook state and do not change SQLite or in-memory schemas.

**Rationale**: Hooks are runtime integration points registered by hosts. Persisting hook state or adding dependency frameworks would add operational burden without current product value.

**Alternatives considered**:

- Durable event outbox: deferred to a later operational slice if durable delivery becomes a requirement.
- Background worker dispatch: rejected because this slice requires synchronous gate/observer semantics.
