# Research: Provider Conformance Tests

## Decision: Test-Only Harness Over Product API

Provider conformance is useful for maintainers and provider authors, but it does not need to become runtime infrastructure. A test-only harness satisfies the current requirement while avoiding a provider registry, framework-style extension point, or additional package surface.

## Decision: Explicit Provider Subjects

Each provider subject supplies its service provider, expected provider key, and fixture requirements explicitly. This keeps behavior discoverable and avoids runtime scanning or automatic discovery.

## Decision: Validate And Execute Unsupported Shapes

Validation must fail closed, but providers remain authoritative during execution. The suite therefore checks both validation and execution for unsupported query shapes so providers cannot rely solely on callers remembering to preflight.

## Decision: Capability-Driven Positive Cases

Supported cases are selected from the provider's declared capabilities. This lets the same suite cover in-memory, SQLite JSON, and minimal custom providers without requiring every provider to support every query shape.

## Decision: Keep Provider-Specific Tests

The conformance suite catches shared contract drift. Provider-specific query semantics, SQL translation details, and edge cases remain in dedicated provider tests.
