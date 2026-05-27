# main Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-05-27

## Active Technologies
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; SQLite provider keeps existing `Microsoft.Data.Sqlite` (003-query-capabilities-typed)
- No new storage; existing in-memory and SQLite JSON providers declare query capabilities (003-query-capabilities-typed)
- N/A for core validation/failure contracts; existing in-memory and SQLite JSON providers keep current persistence behavior (004-provider-validation-execution)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; no new third-party dependencies (005-provider-authoring-ergonomics)
- N/A; no persistence format or storage behavior changes (005-provider-authoring-ergonomics)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting for libraries; tests target net10.0 + Existing xUnit and Microsoft.Extensions.DependencyInjection test stack (006-provider-conformance-tests)
- Existing in-memory store and disposable SQLite JSON database files (006-provider-conformance-tests)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing SQLite JSON provider and test stack (007-sqlite-facet-sorting)
- Existing SQLite JSON payload shape; no migration (007-sqlite-facet-sorting)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK and xUnit test stack; no new dependencies (008-typed-query-authoring)
- N/A; no persistence or schema changes (008-typed-query-authoring)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, in-memory store, SQLite JSON provider, resource manager/store abstractions, query capability/validation stack, portability service, lifecycle hook dispatcher, xUnit test stack; no new dependencies (016-policy-foundations)
- Existing resource definitions gain policy declaration metadata; resource lifecycle markers are stored as additive state separate from immutable resource versions; portable snapshots include policy declarations and lifecycle markers; SQLite JSON adds policy/marker storage without a general migration framework (016-policy-foundations)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, policy declaration/preview models, lifecycle marker service/store, resource definition store, resource version reader, in-memory store, SQLite JSON provider through existing abstractions, xUnit test stack; no new dependencies (017-policy-application-orchestration)
- No schema or storage changes. Application orchestration writes only existing lifecycle marker state through `IResourceLifecycleMarkerStore` after validation and conflict preflight; definitions, resources, activation state, portability snapshots, and SQLite tables remain unchanged. (017-policy-application-orchestration)

- C# / .NET 9.0 (Standard 2.0/2.1 compatible ideally, but targeted for net9.0) + Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging (001-core-sdk-foundation)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# / .NET 9.0 (Standard 2.0/2.1 compatible ideally, but targeted for net9.0)

## Code Style

C# / .NET 9.0 (Standard 2.0/2.1 compatible ideally, but targeted for net9.0): Follow standard conventions

## Recent Changes
- 017-policy-application-orchestration: Added C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, policy declaration/preview models, lifecycle marker service/store, resource definition store, resource version reader, in-memory store, SQLite JSON provider through existing abstractions, xUnit test stack; no new dependencies
- 016-policy-foundations: Added C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, in-memory store, SQLite JSON provider, resource manager/store abstractions, query capability/validation stack, portability service, lifecycle hook dispatcher, xUnit test stack; no new dependencies
- 008-typed-query-authoring: Added C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK and xUnit test stack; no new dependencies


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
