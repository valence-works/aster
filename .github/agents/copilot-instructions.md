# main Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-04

## Active Technologies
- C# with .NET 8/9/10 multi-targeting in core; ASP.NET Core host on .NET 10 + `Microsoft.Extensions.*` abstractions, SQLite ADO.NET provider, `System.Text.Json` for payload serialization (002-roadmap-next-phase)
- SQLite database with JSON document storage semantics (JSON text columns plus relational keys/indexes) (002-roadmap-next-phase)
- C# 13 / .NET 10.0 SDK (library multi-targets `net8.0;net9.0;net10.0`) + `Microsoft.Data.Sqlite` (runtime); `System.Text.Json` (serialisation, already in-tree); `xUnit 2.x` (test) (002-roadmap-next-phase)
- SQLite — file-based; path configurable via `AsterSQLiteOptions` (002-roadmap-next-phase)

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
- 002-roadmap-next-phase: Added C# 13 / .NET 10.0 SDK (library multi-targets `net8.0;net9.0;net10.0`) + `Microsoft.Data.Sqlite` (runtime); `System.Text.Json` (serialisation, already in-tree); `xUnit 2.x` (test)
- 002-roadmap-next-phase: Added C# with .NET 8/9/10 multi-targeting in core; ASP.NET Core host on .NET 10 + `Microsoft.Extensions.*` abstractions, SQLite ADO.NET provider, `System.Text.Json` for payload serialization

- 001-core-sdk-foundation: Added C# / .NET 9.0 (Standard 2.0/2.1 compatible ideally, but targeted for net9.0) + Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
