<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read `specs/012-definition-schema-upgrades/plan.md`.
<!-- SPECKIT END -->

## Active Technologies
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, SQLite JSON provider, xUnit test stack; no new dependencies (009-portable-operators)
- Existing resource payloads; no migration (009-portable-operators)
- Existing resource JSON payloads; no migration or schema changes (010-sqlite-date-ranges)
- Existing resource JSON payloads; no schema migration or physical index creation (011-explicit-indexing-model)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, resource manager/store abstractions, SQLite JSON provider, xUnit test stack; no new dependencies (012-definition-schema-upgrades)
- Existing resource JSON payloads and definition versions; no schema migration or automatic data rewrite (012-definition-schema-upgrades)

## Recent Changes
- 012-definition-schema-upgrades: Added definition schema version and explicit upgrade flow planning; no schema migration or automatic data rewrite
- 011-explicit-indexing-model: Added explicit provider-declared indexing model planning for core SDK contracts; no storage migration or physical indexing
- 009-portable-operators: Added C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, SQLite JSON provider, xUnit test stack; no new dependencies
