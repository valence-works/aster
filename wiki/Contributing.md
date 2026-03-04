# Contributing

Thank you for your interest in contributing to Aster. This page covers the essentials for getting started.

For full coding standards, read [`docs/coding-conventions.md`](../docs/coding-conventions.md).

---

## Getting Started

### Prerequisites

- .NET 10.0 SDK (required for development; the library multi-targets net8/net9/net10)
- An IDE with C# support (Rider, VS, VS Code)

### Build and Test

```bash
# Build the solution
dotnet build Aster.sln

# Run all tests
dotnet test Aster.sln

# Run the workbench
cd src/apps/Aster.Web
dotnet run
```

---

## Project Structure

```
src/
  core/Aster.Core/        -- Main SDK library
  apps/Aster.Web/         -- Workbench (ASP.NET Core minimal API)
test/
  Aster.Tests/            -- xUnit tests (unit + integration)
docs/                     -- Architecture review, coding conventions, roadmap
specs/                    -- Feature specs
wiki/                     -- GitHub wiki source
```

---

## Coding Conventions (Summary)

Full details: [`docs/coding-conventions.md`](../docs/coding-conventions.md)

### Key rules

- `sealed record` for all domain transfer types and version snapshots.
- Immutability — prefer `init`-only properties; return new instances instead of mutating.
- `ValueTask` for all async interface methods.
- `CancellationToken` on every public async method, defaulting to `default`.
- `ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrWhiteSpace` for argument validation.
- File-scoped namespaces (`namespace X;`).
- One public type per file; filename matches type name.
- XML doc comments on all public types and members.

### Naming

| Kind | Convention |
|---|---|
| Interface | `I` prefix, PascalCase — `IResourceManager` |
| Class / Record | PascalCase — `InMemoryResourceManager` |
| Enum | PascalCase, singular — `ComparisonOperator` |
| Async method | `Async` suffix — `CreateAsync`, `ActivateAsync` |
| Private field | camelCase, no underscore — `definitionStore` |

### Target frameworks

- **Libraries** (`Aster.Core`): multi-target `net8.0`, `net9.0`, `net10.0`.
- **Applications / Tests**: target `net10.0` only.

---

## Testing

Tests live in `test/Aster.Tests/`. The project uses **xUnit**.

### Test organisation

```
test/Aster.Tests/
  Definitions/          -- ResourceDefinitionBuilder tests
  InMemory/             -- In-memory store tests (activation, query, manager)
  Integration/          -- End-to-end scenario tests (QuickstartIntegrationTest)
  Services/             -- Binder tests (SystemTextJsonAspectBinder)
```

### Writing tests

- Use `async Task` test methods.
- Name tests: `{Subject}_{Scenario}_{ExpectedResult}`.
- Use direct service construction in unit tests (no test container needed).
- See `QuickstartIntegrationTest` for a full integration test reference.

### Running tests

```bash
dotnet test test/Aster.Tests/Aster.Tests.csproj
```

---

## Spec Workflow

New features follow the **Spec Kit** workflow:

1. **Specify** — create a feature spec in `specs/{id}/spec.md`.
2. **Plan** — generate design artifacts in `specs/{id}/plan.md`.
3. **Tasks** — generate `specs/{id}/tasks.md`.
4. **Implement** — execute tasks against the codebase.
5. **Analyze** — cross-artifact consistency check.

See `specs/001-core-sdk-foundation/` for a complete example.

---

## Pull Request Guidelines

1. All tests must pass (`dotnet test`).
2. No new compiler warnings (`TreatWarningsAsErrors` is enabled).
3. XML doc comments on all new public members.
4. Update relevant wiki pages and/or `docs/` if behaviour changes.
5. Follow naming and code organisation conventions.

---

## License

Contributions are accepted under the MIT License. See [`LICENSE`](../LICENSE).
