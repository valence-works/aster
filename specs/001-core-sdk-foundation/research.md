# Research: Core SDK Foundation

**Feature**: `001-core-sdk-foundation`
**Status**: Consolidated

## Decisions

### 1. Mocking Framework
**Decision**: Use **NSubstitute**.
**Rationale**: NSubstitute offers a cleaner, more readable syntax compared to Moq, reducing boilerplate in unit tests. It is standard in modern .NET ecosystems.
**Alternatives**: Moq (older, more verbose), FakeItEasy.

### 2. In-Memory Storage Structure
**Decision**: Use `ConcurrentDictionary` based collections.
- `ConcurrentDictionary<string, ResourceDefinition>` for Definitions (Key: ID).
- `ConcurrentDictionary<string, List<ResourceVersion>>` for Resource Instances (Key: ResourceID).
- `ConcurrentDictionary<string, ConcurrentDictionary<string, HashSet<int>>>` for Channel Activations (Key: ResourceID -> ChannelName -> VersionNumbers).
**Rationale**: Provides thread-safe access without external database overhead for Phase 1. `List<ResourceVersion>` allows easy version history traversal (append-only).
**Alternatives**: Simple `Dictionary` with locks (more complex to manage correctly).

### 3. Aspect Serialization
**Decision**: Use `System.Text.Json` for POCO conversion.
**Rationale**: Standard .NET library, performant. Aspects stored as `Dictionary<string, object>` or JSON strings in the raw `ResourceVersion`, then deserialized to POCOs on demand.
**Alternatives**: `Newtonsoft.Json` (legacy), or manual mapping.

### 4. Querying LINQ Translation
**Decision**: Implement `IResourceQueryService` using standard `IQueryable` / LINQ to Objects for In-Memory provider.
**Rationale**: In-Memory provider can leverage LINQ directly. The abstraction `IResourceManager` will expose methods that take Expressions or a Query Object.
**Alternatives**: Build a custom query parser (overkill for Phase 1 In-Memory).

## Resolved Unknowns

- **Testing Library**: Confirmed `xUnit` + `NSubstitute`.
- **Target Framework**: Multitarget `net8.0;net9.0;net10.0` (per plan.md §Technical Context; `Aster.Tests` runs on `net9.0` only).

