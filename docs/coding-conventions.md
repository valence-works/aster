# Aster Coding Conventions

This document defines the coding standards and conventions for the Aster project. All contributors must follow these guidelines to maintain consistency and quality across the codebase.

## Table of Contents

- [General Principles](#general-principles)
- [Naming Conventions](#naming-conventions)
- [Code Organization](#code-organization)
- [XML Documentation](#xml-documentation)
- [Dependency Injection](#dependency-injection)
- [Error Handling](#error-handling)
- [Testing](#testing)
- [Configuration & Options](#configuration--options)
- [Observability](#observability)
- [File & Project Structure](#file--project-structure)
- [Version Parsing](#version-parsing)
- [CancellationToken](#cancellationtoken)
- [Code Style Quick Reference](#code-style-quick-reference)

---

## General Principles

1.  **Determinism** — Resource versioning and activation behavior must be deterministic. Given the same definition and input, the system must produce the same result.
2.  **Transactional Safety** — State mutations use atomic operations with clear transaction boundaries. Never leave resource state in an inconsistent position.
3.  **Fail-Safe Defaults** — Options default to the safest value. For example, `AllowAutomaticMigration` defaults to `false`.
4.  **Immutability** — Prefer `sealed record` for data transfer types and resource version snapshots. Use `readonly` fields and properties where possible.

---

## Naming Conventions

### Types

| Kind | Convention | Example |
|------|-----------|---------|
| Interface | `I` prefix, PascalCase | `IResourceService`, `IQueryService` |
| Class | PascalCase, no prefix | `ResourceService`, `DefinitionLoader` |
| Record | PascalCase | `ResourceDefiniton`, `FacetValue` |
| Enum | PascalCase, singular | `ResourceState`, `IndexFieldType` |
| Enum member | PascalCase | `ResourceState.Active` |

### Members

| Kind | Convention | Example |
|------|-----------|---------|
| Public property | PascalCase | `DisplayName`, `VersionId` |
| Private field | camelCase (no underscore prefix) | `resourceOptions`, `writeLock` |
| Parameter | camelCase | `cancellationToken`, `configureOptions` |
| Local variable | camelCase | `resourceDefinition`, `validationErrors` |
| Constant | PascalCase | `DefaultChannelName` |
| Method | PascalCase, verb phrase | `ActivateAsync`, `GetLatestAsync` |
| Async method | `Async` suffix | `ResolveAsync`, `GetActiveAsync` |

### Files

-   One public type per file (exceptions: closely related nested types).
-   File name matches the primary type name: `ResourceService.cs`.
-   Organize files into domain-based folder groupings (e.g., `Definitions/`, `Runtime/`, `Persistence/`).
-   When a folder exceeds ~15 files, split into subfolders (e.g., `Definitions/Models/`, `Definitions/Builders/`).
-   Extension methods go in `Extensions/` subdirectory with `{Feature}ServiceCollectionExtensions.cs` naming.

---

## Code Organization

### Namespace Layout

Use file-scoped namespaces (`namespace X;`). The namespace must mirror the folder path:

```
Aster.Abstractions          — Public contracts, DTOs, enums shared across packages
Aster.Definitions           — Resource, Aspect, and Facet definition models
Aster.Runtime               — Core services, pipelines, versioning/state logic
Aster.Querying              — Query model and query service abstractions
Aster.Indexing              — Indexing strategies and engine
Aster.Persistence           — Storage abstractions
Aster.Persistence.Sqlite    — Specific provider implementation (example)
Aster.Hosting               — Consumer-facing DI registrations & hosted services
```

### Using Directives Order

1.  `System.*` namespaces (implicit via global usings where possible)
2.  `Microsoft.*` namespaces
3.  `Aster.*` namespaces (alphabetical)
4.  Project-local namespaces

No blank lines between groups. Remove unused `using` directives.

---

## XML Documentation

All **public** and **protected** types and members must have XML doc comments:

```csharp
/// <summary>
/// Activates a specific version of a resource in the given channel.
/// </summary>
/// <param name="resourceId">The ID of the resource to activate.</param>
/// <param name="versionId">The specific version ID to activate.</param>
/// <param name="channel">The channel name (e.g., "Published").</param>
/// <returns> The result of the activation operation.</returns>
public ActivationResult Activate(Guid resourceId, Guid versionId, string channel) { ... }
```

-   Use `<see cref="..." />` for cross-references.
-   Use `<see langword="true" />`, `<see langword="false" />`, `<see langword="null" />` for keywords.
-   Use `<inheritdoc />` on interface implementations when the base doc suffices.
-   Document `<exception cref="..." />` for all thrown exceptions.
-   Internal types may omit doc comments but should include a brief `///` summary when the purpose is not self-evident.

---

## Dependency Injection

### Interface Extraction

Extract an `I`-prefix interface for every DI-registered service (e.g., `IResourceService`, `IQueryService`). Register both the concrete type and the interface:

### Registration Pattern

Register concrete types first, then expose them via their interface using a factory delegate:

```csharp
services.AddSingleton<ResourceService>();
services.AddSingleton<IResourceService>(sp => sp.GetRequiredService<ResourceService>());
```

This allows:

-   Resolving the concrete type in integration tests for detailed assertions.
-   Resolving the interface in production code for loose coupling.

### Lifetime Rules

| Type | Lifetime | Reason |
|------|----------|--------|
| Options classes | `Singleton` | Immutable after startup |
| Services | `Singleton` / `Scoped` | Shared runtime components (default to Singleton unless stateful per-scope) |
| Hosted services | `AddHostedService<T>` | Framework-managed |

### Extension Method Conventions

-   One `Add{Feature}` method per feature area.
-   Accept optional `Action<TOptions>?` parameters for configuration.
-   Wire options via `AddOptions<T>()`, register `IValidateOptions<T>`, and call `ValidateOnStart()` for required options.
-   Return `IServiceCollection` for chaining.

---

## Error Handling

### Validation

-   Use `ArgumentNullException.ThrowIfNull(param)` for null guards.
-   Implement options validation through `IValidateOptions<T>` validators.
-   Use `ValidateOnStart()` for required options to fail fast during startup.
-   Keep options classes data-only; do **not** add `IsValid()` methods.

### Exception Types

| Exception | When |
|-----------|------|
| `ArgumentNullException` | Null argument passed to public API |
| `ArgumentException` | Invalid configuration or parameter value |
| `InvalidOperationException` | Operation not valid for current state |
| `ResourceNotFoundException` | Resource or definition not found |
| `OperationCanceledException` | Cancellation token triggered |

### Transient Failure Policy

-   Transient failures (e.g. database connectivity) should use exponential backoff using a resilience policy (e.g. Polly).
-   Max retry attempts and backoff caps should be configurable via options.

---

## Testing

### Test Project Layout

```
test/
  Aster.Runtime.Tests/          — Unit tests for Runtime
  Aster.Persistence.Tests/      — Unit tests for Persistence providers
  Aster.Integration.Tests/      — Integration / contract tests
```

### Test Naming

```
MethodUnderTest_Scenario_ExpectedBehavior
```

Example: `ActivateAsync_WhenVersionIsDraft_SetsActiveState`

For test classes: `{TypeUnderTest}Tests` — e.g., `ResourceServiceTests`.

### Test Structure

Use the **Arrange / Act / Assert** pattern. Keep tests focused on a single behavior.

```csharp
[Fact]
public async Task ActivateAsync_WhenVersionIsDraft_SetsActiveState()
{
    // Arrange
    var sut = CreateService();
    var resource = CreateDraftResource();

    // Act
    var result = await sut.ActivateAsync(resource.Id, resource.VersionId, "Published", CancellationToken.None);

    // Assert
    Assert.True(result.IsSuccess);
    Assert.True(result.Resource.IsActive("Published"));
}
```

### Assertions

-   Use `Assert.Equal`, `Assert.NotNull`, `Assert.Empty`, `Assert.Single`.
-   Prefer `Assert.Single` over `Assert.Equal(1, collection.Count())`.
-   Use `Assert.ThrowsAsync<T>` for expected exceptions.

---

## Configuration & Options

### Options Class Pattern

```csharp
public sealed class AsterOptions
{
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}

internal sealed class AsterOptionsValidator : IValidateOptions<AsterOptions>
{
    public ValidateOptionsResult Validate(string? name, AsterOptions options)
        => options.CacheDuration > TimeSpan.Zero
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("CacheDuration must be greater than zero.");
}
```

-   All options classes are `sealed` data-only types.
-   Provide sensible defaults on options properties.
-   Do not embed validation logic in options classes.
-   Register `IValidateOptions<T>` validators for each options type.
-   Use `ValidateOnStart()` for options required at startup.

---

## Observability

### Logging

-   Use `ILogger<T>` via DI.
-   Use `[LoggerMessage]` source generators for all structured log methods. Define partial methods on the class:

```csharp
[LoggerMessage(
    EventId = 1000,
    Level = LogLevel.Information,
    Message = "Resource activated [ResourceId={ResourceId}, VersionId={VersionId}, Channel={Channel}]")]
private static partial void ResourceActivated(ILogger logger, Guid resourceId, Guid versionId, string channel);
```

-   Log levels:
    -   `Debug` — Detailed decision paths, internal state changes.
    -   `Information` — Major operations (Create, Activate, Publish), startup events.
    -   `Warning` — Degraded state, fallback paths taken, non-critical errors.
    -   `Error` — Unhandled exceptions, data integrity issues.
-   Never log secrets, credentials, or full exception stack traces at `Information` level without sanitization.

### Metrics

-   Use `System.Diagnostics.Metrics` with the `aster.*` namespace.
-   Counter names follow the pattern: `aster.{area}.{metric}` (e.g., `aster.resources.activated`).

---

## File & Project Structure

### Project Dependencies

```
Aster (consumer package)
  └── Aster.Runtime
        ├── Aster.Abstractions
        ├── Aster.Definitions
        ├── Aster.Persistence
        └── Aster.Querying
```

-   **Aster.Runtime** generally orchestrates the core logic.
-   **Aster** (consumer package) owns the hosted service and DI registration that bridges Runtime and Hosting.

### Multi-Targeting

The solution targets `net9.0`, and potentially `net10.0` (preview). Ensure all code compiles against all targets. Use `#if` directives only when absolutely necessary for API differences.

### Build Policy

The root `Directory.Build.props` enforces these settings across all projects:

| Property | Value | Purpose |
|----------|-------|---------|
| `TreatWarningsAsErrors` | `true` | No warnings allowed; every warning is a build error |
| `Deterministic` | `true` | Reproducible builds |
| `Nullable` | `enable` | Nullable reference types enforced project-wide |
| `ImplicitUsings` | `enable` | Common `System.*` namespaces are auto-imported |
| `GenerateDocumentationFile` | `true` (src only) | Enforces XML doc comments on public API surface |

Combined with `TreatWarningsAsErrors`, missing XML documentation on public types/members causes a build failure.

### Central Package Management

Package versions are managed centrally in `Directory.Packages.props`. Never specify versions in individual `.csproj` files — use `<PackageReference Include="..." />` without a `Version` attribute.

---

## Version Parsing

Use shared versioning primitives from `Aster.Abstractions` (or `Aster.Definitions`) for version comparison and selection logic.

-   Avoid hand-rolling semantic version parsing.
-   Use standard comparison logic provided by the core libraries (to be defined in Phase 1).

---

## CancellationToken

-   Always accept `CancellationToken` as the last parameter on `async` methods.
-   Forward the token through the entire async call chain — never drop it.
-   Inside loops that may run for many iterations, call `cancellationToken.ThrowIfCancellationRequested()` to allow prompt cancellation:

```csharp
foreach (var item in items)
{
    cancellationToken.ThrowIfCancellationRequested();
    await ProcessAsync(item, cancellationToken);
}
```

-   Use `CancellationToken.None` only in tests or top-level entry points where no cancellation signal is available.

---

## Code Style Quick Reference

-   **Access modifiers**: Always explicit (`public`, `private`, `internal`).
-   **`var`**: Use when the type is obvious from the right-hand side.
-   **Expression-bodied members**: Use for single-expression methods and properties.
-   **Primary constructors**: Use for record types and simple DI constructors.
-   **File-scoped namespaces**: Always use `namespace X;` (not block-scoped).
-   **Nullable reference types**: Enabled project-wide. Annotate nullability explicitly.
-   **`sealed`**: Seal all classes unless inheritance is an intentional design point.
-   **`readonly`**: Mark fields `readonly` when they are assigned only in the constructor.

