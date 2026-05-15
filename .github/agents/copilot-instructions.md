# main Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-04

## Active Technologies
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; SQLite provider keeps existing `Microsoft.Data.Sqlite` (003-query-capabilities-typed)
- No new storage; existing in-memory and SQLite JSON providers declare query capabilities (003-query-capabilities-typed)
- N/A for core validation/failure contracts; existing in-memory and SQLite JSON providers keep current persistence behavior (004-provider-validation-execution)

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
- 004-provider-validation-execution: Added C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; SQLite provider keeps existing `Microsoft.Data.Sqlite`
- 003-query-capabilities-typed: Added C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; SQLite provider keeps existing `Microsoft.Data.Sqlite`

- 001-core-sdk-foundation: Added C# / .NET 9.0 (Standard 2.0/2.1 compatible ideally, but targeted for net9.0) + Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
