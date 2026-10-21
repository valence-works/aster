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

# Run Aster Studio, the reference host (see its README for the SPA build)
dotnet run --project src/apps/Aster.Studio

# Studio's Playwright smoke tests (they start the host themselves)
cd src/apps/Aster.Studio/ClientApp && npm run build && npm run test:e2e
```

---

## Project Structure

```
src/
  core/Aster.Core/        -- Main SDK library
  hosting/Aster.AspNetCore/ -- Mountable Aster endpoints (MapAster)
  apps/Aster.Studio/      -- Aster Studio: the reference host (ASP.NET Core + React SPA)
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

## PostgreSQL Provider Tests

The store tests in `test/Aster.Tests/Persistence/` run against every persistent provider. The Postgres runs skip unless `ASTER_TEST_POSTGRES` names a server; each test creates and drops its own database there.

```bash
docker run -d --name aster-pg -e POSTGRES_PASSWORD=postgres -p 127.0.0.1:5432:5432 postgres:16
export ASTER_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres"
dotnet test Aster.sln
```

CI sets `ASTER_REQUIRE_POSTGRES=1` as well, so a missing server fails the run instead of skipping.

## SQL Server Provider Tests

The same store, query and conformance tests run against SQL Server when `ASTER_TEST_SQLSERVER` names a server; each test creates and drops its own database there, and the runs skip otherwise.

```bash
docker run -d --name aster-mssql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Aster_Pass123!' -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest
export ASTER_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=Aster_Pass123!;TrustServerCertificate=True"
dotnet test Aster.sln
```

CI runs SQL Server 2022 as a service container and sets `ASTER_REQUIRE_SQLSERVER=1`, so a missing server fails the run instead of skipping.

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
