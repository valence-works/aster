# Feature Specification: Core SDK Foundation (Phase 1)

**Feature Branch**: `001-core-sdk-foundation`
**Created**: 2026-03-04
**Status**: Draft

## 1. Introduction

This feature implements **Phase 1** of the Aster roadmap, establishing the foundational SDK and In-Memory engine. It provides the core domain models, definition registry, versioning mechanics, and a typed aspect system required to build composable resources.

The goal is to deliver a working In-Memory implementation that allows developers to define, create, version, and query resources without external persistence dependencies.

## Clarifications

### Session 2026-03-04
- Q: Active vs Published semantics? → A: **Configurable Multi-Active** (activation accepts `allowMultipleActive` flag).
- Q: Typed Aspect save behavior? → A: **State Replace** (POCO is source of truth; replaces dictionary content).
- Q: Concurrency handling during activation? → A: **Optimistic Resource Lock** (check Resource ETag/Version; fail on concurrent modification).

## 2. User Scenarios

### 2.1. Defining a Resource Type (Code-First)

**Actor**: Developer
**Goal**: Define a "Product" resource with dynamic aspects.

1.  Developer uses `IResourceDefinitionBuilder` to define a resource type `Product`.
2.  Developer attaches a `TitleAspect` (unnamed).
3.  Developer attaches a `PriceAspect` (unnamed).
4.  Developer attaches a `TagAspect` twice (named "Categories" and "Badges").
5.  System registers the definition in the `IResourceDefinitionStore`.

### 2.2. Creating and Versioning a Resource

**Actor**: Developer / System
**Goal**: Create a product, update it, and publish it.

1.  User creates a new `Product` resource (Version 1, Draft).
2.  User sets the Title to "Super Gadget".
3.  User saves the draft. System persists Version 1.
4.  User updates the Title to "Super Gadget Pro" (Version 2, Draft).
5.  User activates Version 2 in the "Published" channel.
6.  System marks Version 2 as Active in "Published". Version 1 remains inactive.

### 2.3. Using Typed Aspects (POCOs)

**Actor**: Developer
**Goal**: Work with strong types instead of raw dictionaries.

1.  Developer defines a C# record `PriceAspect(decimal Amount, string Currency)`.
2.  Developer registers this type with Aster.
3.  Developer loads a resource and requests the `PriceAspect`.
4.  System deserializes the underlying aspect data into the `PriceAspect` POCO.
5.  Developer modifies the POCO and saves the resource.
6.  System serializes the POCO back into the resource version payload.

### 2.4. Basic In-Memory Querying

**Actor**: Developer
**Goal**: Find resources using the query abstraction.

1.  Developer constructs a `ResourceQuery` filtering by `ResourceType == 'Product'` and `TitleAspect.Title contains 'Gadget'`.
2.  Developer executes the query via `IResourceQueryService`.
3.  System (In-Memory provider) translates the query to LINQ and returns matching resources.

## 2.5. Edge Cases & Error Handling

**Scenario**: Duplicate Aspect Attachment
*   **Given**: A resource definition with an unnamed `TitleAspect`.
*   **When**: Developer tries to attach another `TitleAspect` without a name.
*   **Then**: System throws a validation error (Duplicate unnamed attachment).

**Scenario**: Invalid Activation
*   **Given**: A resource with only Version 1.
*   **When**: Developer tries to activate Version 99.
*   **Then**: System throws `VersionNotFoundException`.

**Scenario**: Concurrent Modification (Optimistic Locking)
*   **Given**: Version 1 of a resource.
*   **When**: Two threads try to save a new Version 2 based on Version 1 simultaneously.
*   **Then**: One succeeds; the other fails with a `ConcurrencyException`.

## 3. Functional Requirements

### 3.1. Domain Models
*   **Definitions**: Must provide models for `ResourceDefinition`, `AspectDefinition`, and `FacetDefinition`.
*   **Instances**: Must provide models for `Resource`, `ResourceVersion`, `AspectInstance`, and `FacetValue`.
*   **Attachments**: Must support attaching Aspect Definitions to Resource Definitions, optionally with a `Name` discriminator.
*   **Versioning**: Versions must be immutable. Every save of a modified draft creates a new `ResourceVersion`.

### 3.2. Definition Registry
*   Provide a **Fluent API** to define resources and aspects in code.
*   Validate uniqueness of Aspect Attachments (by ID and Name) within a Resource Definition.
*   Support creating/updating definitions at runtime.

### 3.3. In-Memory Engine
*   Provide a service for Create, Save (Draft), and Get operations.
*   Implement **Channel-Based Activation**:
    *   Support activating a specific version in a named channel (e.g., "Published").
    *   **Configurable Multi-Active**: The activation operation must accept a flag (e.g., `allowMultipleActive`) to determine if existing active versions in that channel should be deactivated (Single-Active) or kept (Multi-Active).
    *   **Optimistic Concurrency**: Activation requires checking the Resource's current version/ETag. If it has changed since the read, the operation must fail with a `ConcurrencyException`.
    *   Support deactivating a version from a channel.
*   Retrieval:
    *   Get Latest: Retrieve the chronologically latest version.
    *   Get Active: Retrieve the version active in a specific channel.

### 3.4. Typed Aspects
*   Provide a mechanism to map a C# Type/Record (POCO) to an `AspectDefinition`.
*   Provide a binding mechanism to convert between the internal storage format (dictionary-like) and the strong C# Type.
*   **State Replace**: When saving a Typed Aspect POCO, the system must replace the entire dictionary state for that aspect instance, effectively treating the POCO as the exclusive source of truth.
*   Ensure round-trip serialization works for standard primitives (string, int, bool, date, decimal).

### 3.5. Query Model Contracts
*   Define a portable **Query AST** (Abstract Syntax Tree) including:
    *   Filters (Metadata, Aspect Presence, Facet Values).
    *   Logical Operators (AND, OR, NOT).
    *   Comparisons (Equals, Contains, Range).
*   Define a service contract for executing queries.
*   Implement a reference **In-Memory Evaluator** that executes these queries against objects in memory.

### 3.6. Workbench Application
*   A standalone Web Application acting as a test harness.
*   Must host the Core SDK and In-Memory Engine.
*   Provide a user interface to visualize Definitions and Resource Instances for verification.

## 4. Technical Constraints

*   **No Persistent Database**: The initial implementation must be strictly In-Memory.
*   **Concurrency**: The system must handle concurrent writes safely (e.g., using safe collections/locks).
*   **Architecture**: Must follow the project's layered architecture (Abstractions -> Definitions -> Runtime).

## 5. Success Criteria

*   **Tests**: Unit tests covering 90% of the Domain Model logic (Activation, Versioning).
*   **Scenario**: A developer can successfully define a resource type, create an instance, and retrieve it using a Typed Aspect POCO.
*   **Performance**: The In-Memory engine handles defined load (e.g., 10k reads/sec) without errors.
*   **Completeness**: All "Definition of Done" criteria from the Roadmap Phase 1 are satisfied.

## 6. Assumptions

*   In-Memory data is volatile and will be lost on application restart.
*   Workbench UI is for developer/internal use only.
*   Only "Safe" query operators (Equals, Contains) need to be supported in the initial In-Memory evaluator.
