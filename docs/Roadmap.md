# Aster — Architecture & Roadmap for Spec-Kit

This roadmap is written as a **spec-generation scaffold**: each phase contains clear epics, concrete deliverables, and “Definition of Done” criteria that can be turned into Spec Kit feature specs.

---

## 0) Vision, Goals, and Non-Goals

### Vision
**Aster** is a .NET library/framework for **composable, versioned resources** defined at runtime (and/or by code) using a **Resource → Aspect → Facet** model.

### Core goals
- Provide an **SDK** to define:
    - **Resource Definitions** (schemas)
    - **Resources** (instances)
    - **Aspect Definitions / Aspects**
    - **Facet Definitions / Facets**
- Resources are **draftable and versionable** by default (Orchard-style), but with a more general concept of **activation**:
    - Versions can be marked **Active/Enabled** (potentially more than one).
    - “Published” becomes one possible activation channel (host-defined semantics).
- Support **typed Aspects** implemented as C# classes.
- Provide a **query model** that enables filtering by:
    - Resource metadata (type, state, version)
    - Aspect/facet values (including typed aspects)

### Non-goals (explicit)
- No “shape rendering” / CMS UI framework in Aster.
- No hard dependency on Orchard Core or YesSQL.
- No hard dependency on a single database engine.

### Host integration
- Hosts (e.g., Elsa Studio) can build a tailored UI module that renders/edit resources based on definitions.
- Aster’s job is: **definitions, storage model, versioning/state, querying surface, and extensibility points**.

---

## 1) Domain Model and Terminology

### Definitions vs Instances
- **Resource Definition**: schema for a resource type (e.g., `WorkflowDefinition`, `Agent`, `Secret`, `Product`).
- **Resource**: instance of a resource definition; has versions.
- **Aspect Definition**: definition of an attachable “part” (e.g., `Tags`, `RBAC`, `Deployable`, `Owner`, `Seo`).
- **Aspect**: instance of an aspect on a resource version.
- **Facet Definition**: definition of an individual field on an aspect (e.g., `Tags: Text[]`, `OwnerId: Guid`).
- **Facet**: the actual value(s) for a facet.

### Named / repeated aspects (Orchard “Named Parts”)
Aster supports attaching the same Aspect Definition more than once to a resource by using **Named Aspects**.

- When an Aspect Definition is attached to a Resource Definition, it gets an **attachment identity**:
    - `AspectAttachmentId` (stable GUID) and optional `Name` (string).
- A resource version can contain multiple aspect instances of the same Aspect Definition, distinguished by `AspectAttachmentId` (and optionally `Name`).

**Baseline invariant**
- Unnamed attachment: at most one instance.
- Named attachment: multiple instances allowed; `Name` must be unique per resource definition among attachments of the same aspect definition.

**Attachment vs instance (important for querying)**
- Queries should primarily target **attachments**, not just definitions:
    - “Has Tags aspect attached (unnamed)”
    - “Has Owner aspect attached with name = PrimaryOwner”
    - “Has Owner aspect attached (PrimaryOwner).OwnerId == X”

### Identity & invariants (spec-ready contract)
These rules should be treated as **hard invariants** that persistence/query providers must preserve.

#### IDs
- `ResourceDefinitionId` (Guid): identity of a resource definition across its versions.
- `ResourceDefinitionVersionId` (Guid): immutable snapshot id.

- `AspectDefinitionId` (Guid): identity of an aspect definition across its versions.
- `FacetDefinitionId` (Guid): identity of a facet definition across its versions.

- `ResourceId` (Guid): identity of the resource across its versions.
- `ResourceVersionId` (Guid): identity of a specific resource version.

- `AspectAttachmentId` (Guid): identity of “aspect X attached to resource definition Y (optionally named)”.
    - This is the key for supporting **named/repeated aspects**.

#### Uniqueness / cardinality
- For a given `ResourceDefinitionVersionId`:
    - `ResourceType` must be unique within its definition scope (e.g., tenant/module).
    - A given `AspectAttachmentId` is unique.
    - For attachments of the same `AspectDefinitionId`, `Name` (if provided) must be unique.
- For a given `ResourceVersionId`:
    - An aspect instance is uniquely identified by `(AspectAttachmentId)`.
    - Facets within an aspect are uniquely identified by `(FacetName)` (or facet id, depending on implementation).

#### Integrity
- A `ResourceVersion` references exactly one `ResourceDefinitionVersionId` (schema snapshot).
- A `Resource` may contain versions referencing different schema versions (after upgrades).

### Versioning & state model (baseline)
Aster uses **immutable (append-only) resource versions**.

- Resource has a stable `ResourceId` (identity across versions).
- Each version has a `VersionId` and ordinal.
- Draft handling:
    - Multiple drafts are supported.
- Latest:
    - `Latest` can also be `Active`.

#### Activation model (spec-ready contract)
Aster treats activation as a **set of activation records per version**, keyed by `Channel`.

- Channel names are case-insensitive (recommend canonical form enforced by host), but stored as provided.
- A version can be active in **0..N channels**.
- A channel may allow **multiple active versions simultaneously** (default) or enforce single-active (optional policy; host/provider-defined).

**Core flags/fields (proposed)**
- `IsLatest` (1 per resource)
- `IsDraft` (derived: not active + not published + not archived, or explicit)
- `IsArchived` (optional)
- `Activation`: a set of activation records on a version:
    - `{ Channel: string, IsActive: bool, ActivatedUtc, ActivatedBy }`

**Recommended minimal required queries**
- “Latest version”
- “All drafts” (optionally filtered by author/branch)
- “All active in channel X”
- “The most recently activated in channel X” (useful when a channel is effectively single-active)

**Operations**
- Create draft (new version)
- Save draft (new version)
- Activate version in channel(s)
- Deactivate version in channel(s)
- Promote historical version (activate an older version)
- Archive/prune policies (optional)

**DoD (state semantics)**
- Deterministic rules for `Latest`, `Drafts`, and activation transitions.
- First-time activation of an unpublished resource is just `Activate` in a chosen channel (often “Published”).

---

## 2) Primary Use Cases to Anchor Design

Aster exists to make **horizontal capabilities** reusable and attachable, without forcing every entity type to be hand-modeled for each capability.

### Core cross-cutting aspects (examples)
- **Tags**: add tagging to anything (workflows, agents, secrets, products).
- **RBAC / ACL**: per-resource permissions (view/edit/delete) with optional inheritance.
- **Owner**: link to a user/subject; drives filtering and permission defaults.
- **Deployable**: associate resources with deployment targets/environments.
- **Auditing**: created/modified timestamps + actor identifiers.
- **Scheduling**: activate at / deactivate at windows.
- **Localization**: per-culture variants (optional, later).
- **Soft-delete / Retention**: lifecycle management policies.
- **Portability**: export/import between environments (core primitives) with optional higher-level recipe modules.
- **Search / Indexable**: declares what should be queryable.

---

## 3) Roadmap Overview

- **Phase 1 — Core SDK & In-Memory Engine (Foundation)**
- **Phase 2 — Persistence & Querying (Essentials)**
- **Phase 3 — Advanced Indexing & Typed Querying**
- **Phase 4 — Portability & Integration Hooks (Core) + Optional Recipe Modules**
- **Phase 5 — Multi-tenancy + Advanced Versioning + Policies**
- **Phase 6 — Operational Hardening (migrations, concurrency, perf)**

Each phase below is structured as Spec Kit-friendly epics.

---

# Phase 1 — Core SDK & In-Memory Engine (Foundation)

## Epic 1.1 — Core Contracts and Domain Types
**Deliverables**
- `ResourceDefinition`, `AspectDefinition`, `FacetDefinition` models
- `Resource`, `ResourceVersion`, `AspectInstance`, `FacetValue` models
- Attachments:
    - `AspectAttachment` (aspect definition attached to a resource definition)
    - `FacetAttachment` (facet definition attached/overridden in an aspect attachment)
- Named aspect support:
    - `AspectAttachmentId`, `Name`
    > **Architectural Note:** Named aspects significantly increase query complexity. While supported in the domain model, their use in querying might be restricted in early phases.
- Metadata model: `ResourceType`, `DisplayName`, `Description`, settings
- State model primitives:
    - activation record(s) per version

**DoD**
- Clear separation of definitions vs instances
- JSON-serializable domain objects
- Validation rules:
    - unique names
    - aspect attachment uniqueness per resource definition (including `Name`)
    - facet uniqueness per aspect attachment
    - deterministic settings layering

## Epic 1.2 — Definition Registry and Builder APIs
**Deliverables**
- Fluent SDK for definitions (code-first):
    - `IResourceDefinitionBuilder`
    - `IAspectDefinitionBuilder`
    - `IFacetDefinitionBuilder`
- Attachment configuration APIs:
    - attach aspect definition → resource definition with settings + optional name
    - allow facet override settings at attachment-time
- Runtime definition interface:
    - `IResourceDefinitionStore` (CRUD)
    - `IAspectDefinitionStore` (optional split) or unified

**DoD**
- Create/Update resource definitions at runtime
- Attach aspects to resource definitions (named + unnamed)
- Configure attachment settings at attach-time
- Add facets to aspects
- Validate definition changes

## Epic 1.3 — In-Memory Resource + Versioning/State Behavior
**Deliverables**
- In-memory resource/version store
- `IResourceService` with operations:
    - Create resource (initial draft version)
    - Save draft (new version)
    - List drafts
    - Activate/deactivate version per channel
    - Get latest / get active by channel / get by version
    - List resources by type + basic filters

**DoD**
- Immutable versions implemented and tested
- Multiple drafts supported
- Deterministic behavior for activate/deactivate flows
- Basic filtering works without aspect querying

## Epic 1.4 — Workbench Application (Sample & Playground)
**Deliverables**
- A sample application (`Aster.Workbench.Web`) that consumes the core SDK:
    - demonstrates defining resource definitions and aspects in code
    - demonstrates creating, saving, and activating resources
- Acts as a "living documentation" for developer usage.

**DoD**
- Developers can clone and run the workbench to see Aster working in-memory.
- Code patterns in the workbench align with recommended usage.

## Epic 1.5 — Typed Aspects Foundation (Moved from Phase 4)
**Rationale**
Moving typed aspects to Phase 1 ensures the "developer experience" is validated early. We don't want to build a "stringly-typed" engine and then bolt on types later.

**Deliverables**
- Typed aspect mapping attributes/fluent API:
    - Bind `TAspect` (POCO) ↔ `AspectDefinitionId`
    - Bind property ↔ facet name
- Serialization/Binder:
    - `AspectInstance` (dictionary) ↔ `TAspect` (object) serialization
- Validation hooks per typed aspect

**DoD**
- Developers can define an aspect as a C# class
- Saving a resource respects the typed structure
- Round-trip (Save -> Load -> Activate) works with POCOs

## Epic 1.6 — Query Model Contracts
**Rationale**
Defining the query contract early prevents the persistence layer (Phase 2) from painting itself into a corner.

**Deliverables**
- `ResourceQuery` object model (the "AST" for queries)
    - Filters: Metadata, Aspect Subscription, Facet Value
    - Operators: Eq, Contains, Range, etc.
- `IResourceQueryService` interface definition
- **NO implementation** required yet (in-memory implementation can be naive LINQ)

**DoD**
- `ResourceQuery` classes exist and are expressive enough for the core use cases.
- In-memory store supports basic `ResourceQuery` execution via LINQ.

---

# Phase 2 — Persistence & Querying (Essentials)

## Epic 2.1 — Persistence Abstractions (Write Model)
**Deliverables**
- Storage interfaces:
    - `IResourceWriteStore`
    - `IResourceReadStore` (basic)
    - `IUnitOfWork` (optional)
- Serialization strategy:
    - a resource version as a “document” referencing a specific **Resource Definition Version**

**DoD**
- Host can plug in persistence
- No coupling to EF/YesSQL/Mongo
- Clear persistence shape (document-like)

## Epic 2.2 — Reference Backend #1 (choose one)
Pick one backend to prove the design. Recommended for speed:
- **SQLite + JSON** (simple) OR
- **PostgreSQL + JSONB** (realistic) OR
- **MongoDB** (document-native)

**Deliverables**
- A single production-grade backend package:
    - `Aster.Persistence.<Backend>`
- Migrations/bootstrap for required tables/collections

**DoD**
- Resources + definitions persisted across restarts
- Activate/deactivate and version retrieval works
- Concurrency strategy defined (at least optimistic)

## Epic 2.3 — Query Surface Implementation
**Deliverables**
- Implementation of `IResourceQueryService` for the chosen backend (Epic 2.2)
- Translation from `ResourceQuery` AST to provider-specific usage (SQL/JSON filters)
- Paging/sorting support

**DoD**
- Can filter resources by metadata and simplistic aspect values
- Basic operators (`Equals`, `Contains`, `Range`) work against the persistence layer.

## Epic 2.4 — Provider migrations / provisioning (per provider module)
Aster should treat database setup as a **provider responsibility**.

**Principles**
- Provider modules own their own database lifecycle:
    - SQL: migrations (tables/constraints/indexes)
    - Document: provisioning (collections/indexes/unique constraints)
- Aster core defines *abstractions and conventions*, but does not embed a specific migration engine.
- Providers MAY use FluentMigrator (recommended for SQL) or another engine, as long as they implement Aster’s core contracts.

### Core terminology (portable)
To avoid forcing document stores into SQL mental models:
- Use **Infrastructure Step** as the umbrella term.
- A SQL provider implements infrastructure steps as **migrations**.
- A document provider implements infrastructure steps as **provisioning steps**.

**Aster core contracts (provider-agnostic)**
- `IInfrastructureStep` (preferred name; can be introduced as an alias of `IMigration`):
    - unique step id (monotonic version or timestamp)
    - `Up()` (apply)
    - optional `Down()` (provider choice)
    - name / tags / optional “requires downtime” marker
- `IInfrastructureStepSource` / `ICatalog` to discover steps from assemblies
- `IInfrastructureStateStore` to record applied steps (table/collection name is provider-defined)
- `IInfrastructureRunner` (can remain named `IMigrationRunner` initially if you prefer):
    - `ApplyAsync(targetVersion?)`
    - `GetPendingAsync()`
    - `GetAppliedAsync()`
    - optional: `VerifyAsync()` (validate required infra exists)

> Note: If you want to keep names stable early, keep the existing `IMigration*` naming but document it as "infrastructure steps".

### SQL providers (relational)
**Guidance**
- For SQL backends, use a mature migration engine (recommended: FluentMigrator) to manage tables, constraints, and indexes.

**Recommended FluentMigrator integration pattern**
- Keep FluentMigrator types isolated inside the provider module.
- Implement Aster’s runner by delegating to FluentMigrator:
    - provider defines migrations using FluentMigrator’s `Migration` classes
    - provider implements `IMigrationRunner` (or `IInfrastructureRunner`) using FluentMigrator’s runner + its version table
- Aster-facing abstraction is stable even if the provider swaps migration engines later.

### Document providers (e.g., MongoDB)
Document stores typically don’t have schema migrations in the same sense, but they often need deterministic provisioning.

**Guidance**
- Prefer a **schema provisioning** approach:
    - create collections
    - create/ensure indexes
    - create/ensure unique constraints (where supported)
    - optional: background data migrations for materialized projections

**Suggested naming**
- `Aster.Persistence.Mongo`
- optional helper module:
    - `Aster.Persistence.Mongo.Provisioning` (collection/index provisioners)
    - optional hosting glue: `Aster.Hosting.Mongo` (startup runner wiring)

**Provider contract clarification**
- SQL providers implement infrastructure steps via a migration engine.
- Document providers implement infrastructure steps as **idempotent ensure operations**.
- If a backend has no meaningful migration concept, it must still be able to "initialize" and "verify" required infrastructure.

**Deliverables (per provider module)**
- Provider implementation of:
    - runner (`IMigrationRunner` / `IInfrastructureRunner`)
    - state store (`IMigrationStateStore` / `IInfrastructureStateStore`)
- A compiled step set that covers:
    - write model tables/collections
    - index/projection tables/collections (if applicable)

**Optional module: startup runner**
Providers (or a small optional package like `Aster.Hosting.Migrations`) may ship an `IHostedService` that:
- runs infrastructure steps on startup when enabled
- delegates to the runner

**Requirement**
- The `IHostedService` must be thin and extracted so that host apps can:
    - disable auto-run
    - execute steps explicitly (CLI, deploy pipeline, etc.)

**DoD**
- Provider package can initialize its schema/infrastructure in an empty database.
- Provider package can upgrade/ensure from version N → latest.
- Host can choose between:
    - auto-run at startup via `IHostedService`, or
    - manual execution.

---

# Phase 3 — Advanced Indexing & Typed Querying

This phase extends the basic query model with provider capability negotiation, advanced text handling, and typed access patterns.

## Epic 3.1 — Advanced Indexing Logic & Capabilities
**Goal**
Allow providers to expose specific capabilities (like full-text search vs. substring) and handle advanced indexing scenarios.

**Deliverables**
- `IQueryCapabilities` service
- Index Field Model (portable types like `NormalizedText`, `Keyword`, `DateTime`)
- Query Planner (validates queries against capabilities)
- Portable normalization logic (for text/numbers)

**DoD**
- Provider can reject unsupported queries gracefully.
- Text normalization ensures consistent behavior across backends.

### Index field model (Spec boundary)
Aster’s query/indexing system needs a small, explicit model of index field types so providers can implement consistently.

#### IndexFieldType (proposed)
Providers MAY support additional types, but Aster’s portable surface should be based on:
- `Keyword` — exact match, optionally case-insensitive normalization
- `Text` — full-text-ish search (tokenization/analyzers are provider-specific)
- `NormalizedText` — “contains” with deterministic normalization (e.g., lowercased, culture-invariant) when provider can’t do full text
- `Boolean`
- `Integer` (64-bit)
- `Decimal` (double/decimal; define precision expectations per provider)
- `DateTime` (UTC)
- `Guid`
- `KeywordArray` — array of keyword values
- `GuidArray` / `IntegerArray` etc. (optional; can be normalized to a generic “Array of T” model)

#### Field naming / addressing
Index fields are addressed by a stable identifier derived from:
- `(AspectAttachmentId, FieldName)` for attachment-scoped fields
- Optional: `(AspectAttachmentId, FieldName, Culture)` later for localization

**Requirement**
- Field identity must be stable across resource versions and independent of backend storage naming.

### Operator support matrix (portable semantics)
Aster should define portable operator semantics and allow providers to advertise what they support.

**Operators (portable set)**
- `Exists(field)`
- `Equals(field, value)`
- `NotEquals(field, value)`
- `In(field, values[])`
- `Range(field, min?, max?, includeMin, includeMax)` for numeric/date
- `Contains(field, value)` for `Text`/`NormalizedText`
- `StartsWith(field, value)` for `Keyword`/`NormalizedText`

#### Exists / missing / null semantics (portable)
Providers differ in how “missing fields” and `null` values are represented (SQL joins, JSON documents, sparse indexes). Aster should define a portable baseline.

**Definitions**
- *Missing*: the facet/index field has no stored value for this resource version.
- *Null*: the field exists but has an explicit `null` value (only applicable to nullable scalar fields).
- *Empty*: the field exists but has an “empty” value (e.g., empty string, empty array).

**Portable semantics**
- `Exists(field)` returns `true` when the field is present and non-null.
    - For scalars: value is not null.
    - For arrays: the array contains at least one element.
- `Not Exists(field)` returns `true` when the field is missing OR null OR (for arrays) empty.

**Implications**
- Empty arrays are treated as “not exists” by default. This avoids false positives in common queries like “has any tags”.
- Hosts that need to distinguish *missing* vs *empty* should model it explicitly (e.g., separate boolean facet `HasTags`), or use provider-specific capabilities.

**Capability extension (optional)**
Providers may expose finer-grained operators:
- `IsNull(field)` / `IsMissing(field)` / `IsEmpty(field)`
These are not portable by default and should be behind capability checks.

**DoD**
- Aster query planner enforces these semantics consistently and documents provider differences via `IQueryCapabilities`.

### Array/list operators (portable set)
Multi-valued facets are common (tags, roles, categories). Backends vary widely, so Aster should define explicit operators.

- `ArrayContains(field, element)`
    - True if the array contains at least one element equal to `element`.
- `ArrayContainsAny(field, elements[])`
    - True if the array contains any of the requested elements.
- `ArrayContainsAll(field, elements[])`
    - True if the array contains all requested elements.
- `ArrayLength(field, op, value)` (optional, later)
    - e.g., length >= 2

**Canonical semantics**
- Equality for array elements uses the field’s element type semantics:
    - `KeywordArray`: keyword equality (plus any declared normalization)
    - `GuidArray`: Guid equality
    - numeric arrays: numeric equality
- Array operators are **order-insensitive**.
- Arrays may contain duplicates; duplicates do not affect `ContainsAll/Any` semantics.

#### Array indexing model (portable)
To keep provider implementations feasible:
- `ArrayContains(field, x)` should be implementable by indexing array elements as repeated values (e.g., join table, inverted index, or native array types).
- Providers that can only support `ArrayContains` but not `ArrayContainsAll` must report that via capabilities.

#### Array capability flags (recommended)
`IQueryCapabilities` should report for array fields:
- Supported array operators (`Contains`, `ContainsAny`, `ContainsAll`)
- Max elements in an `ArrayContainsAny/All` query (optional)
- Whether arrays are stored/indexed as:
    - `NativeArray`
    - `RepeatedField`
    - `JoinTable`
    - `ProviderDefined`

#### Array rewrite guidance
- `ArrayContainsAny(field, [a,b,c])` may be rewritten to `OR(ArrayContains(field,a), ArrayContains(field,b), ...)` when supported and within query complexity limits.
- `ArrayContainsAll(field, [a,b])` may be rewritten to `AND(ArrayContains(field,a), ArrayContains(field,b))` when semantics are equivalent for the provider.
    - Note: this is generally equivalent for sets, but verify provider behavior for null/missing fields.


### Provider capability negotiation
Because not all backends support the same operators (e.g., substring vs fulltext), Aster needs an explicit capability mechanism.

**Deliverables**
- `IQueryCapabilities` (or similar) exposed by a provider/index engine:
    - supported field types
    - supported operators per field type
    - text semantics descriptors (tokenization, case sensitivity, diacritics)
    - max query complexity limits (optional)
- Query planner/validator that can:
    - validate a `ResourceQuery` against capabilities before executing
    - optionally rewrite queries into equivalent forms when possible (e.g., `Contains` → `NormalizedText` fallback)

**DoD**
- Given a query + provider capabilities, the system either:
    - executes successfully, OR
    - fails with a clear, typed error describing unsupported feature + location in the query

#### Query planning & rewrite policy (recommended)
Aster should validate queries against provider capabilities **before execution**, and may optionally rewrite queries when (and only when) semantics remain equivalent.

**Rewrite modes (proposed)**
- `None`
    - Never rewrite. If the provider can't execute the query, fail with a capability error.
- `SafeOnly` (recommended default)
    - Only rewrite when equivalence is guaranteed.
- `OptInExtended`
    - Allow non-equivalent rewrites only with explicit host opt-in (e.g., for convenience), and mark them in diagnostics.

**Safe rewrite rules (equivalence-preserving)**
- `Contains(TextField, value)` → `Contains(NormalizedTextField, value)` **only if**:
    - the index/mapping explicitly provides a corresponding `NormalizedText` field for the same logical property, AND
    - the provider reports `TextMatchSemantics = Substring` support for that field, AND
    - normalization routine is known/declared (portable normalization).
- `StartsWith(KeywordField, value)` → `StartsWith(NormalizedTextField, value)` when a normalized field exists and is declared equivalent.
- `Equals(KeywordField, value)` → `Equals(NormalizedTextField, Normalize(value))` when the normalized field is declared as “normalized keyword”.

**Forbidden rewrites (not equivalent)**
- Any rewrite between `Text` (token/analyzer semantics) and substring matching when the provider’s semantics differ.
- Any rewrite that changes:
    - case/diacritics sensitivity
    - tokenization
    - culture-specific parsing rules

**Diagnostics requirements**
- The planner must return diagnostics that include:
    - whether rewrites occurred
    - which predicates were rewritten
    - which capability triggered the rewrite or prevented execution

**Error behavior**
- If rewrite is not possible under the chosen rewrite mode and capabilities are insufficient, fail fast with a typed error (e.g., `UnsupportedQueryFeatureException`) that points to the exact predicate/operator.

#### Text capability flags (recommended)
To make behavior non-ambiguous across providers, `IQueryCapabilities` should expose a small set of explicit flags for text fields.

**Suggested enums/flags**
- `TextMatchSemantics`
    - `Substring` — raw substring matching (portable expectation for `NormalizedText`)
    - `Token` — token/analyzer-based matching (typical `Text` semantics)
    - `Hybrid` — provider may combine substring + token semantics (must be documented)
- `TextCaseSensitivity`
    - `Sensitive`
    - `Insensitive`
    - `ProviderDefault`
- `TextDiacriticsSensitivity`
    - `Sensitive`
    - `Insensitive`
    - `ProviderDefault`
- `TextTokenizer`
    - `None` (no tokenization)
    - `Standard` (word tokenization)
    - `ProviderDefined`

**Guidance**
- `NormalizedText` should report `TextMatchSemantics = Substring` and `TextTokenizer = None`.
- `Text` should typically report `TextMatchSemantics = Token` and `TextTokenizer != None`.
- If a provider reports `ProviderDefault` for sensitivity flags, Aster should treat it as “not portable” and require the host to opt in explicitly.

### Portable value normalization (Spec boundary)
In practice, cross-provider querying fails when values are normalized differently (UTC handling, decimal precision, text casing/whitespace). Aster should define a portable minimum so that `Equals` and `Range` behave consistently.

#### DateTime normalization
- All DateTime values are treated as **UTC instants**.
- Aster should store and index DateTime as UTC. If an incoming value has an offset, it must be converted to UTC.
- Providers must not apply implicit timezone conversions.

**Recommended portable input forms**
- `DateTime`/`DateTimeOffset` inputs at the API layer.
- If accepting string inputs for migrations/import only:
    - require ISO-8601 with offset or `Z`.

#### Decimal/number normalization
- Aster should distinguish:
    - `Integer` (64-bit) for exact integers
    - `Decimal` for fractional numbers
- Providers must document precision/rounding behavior for `Decimal`.

**Portable guidance**
- Prefer `Integer` for identity-like values.
- For `Decimal` equality, avoid provider-specific floating rounding by:
    - preferring `decimal` at the API layer, OR
    - rounding to a declared scale on write/index (provider capability).

#### Guid normalization
- Store/index GUID values in a canonical binary form when possible.
- If serialized as text (e.g., portability packages), use lowercase `D` format.

#### Text normalization
Text behavior is split between `Text` (provider-defined) and `NormalizedText` (portable).

**Portable normalization for NormalizedText (recommended)**
- Unicode normalization: NFC (recommended)
- Case folding: culture-invariant lowercase
- Whitespace handling:
    - trim leading/trailing whitespace
    - treat whitespace-only as empty

**Exists semantics for strings**
- Empty or whitespace-only normalized strings should be treated as **not exists** (consistent with empty arrays).

**DoD**
- Query planner and index mapping use the same normalization routine definition.
- Providers that can't implement the normalization must expose a capability limitation.

## Epic 3.2 — Querying Typed Aspects (Was Epic 4.2)
**Deliverables**
- Mapping from typed properties to index fields
- Strongly-typed query helpers:
    - `WhereAspect<TAspect>(...)` that compiles into the query model (not raw LINQ)
    - supported expression subset is explicitly defined and validated
- Expression parsing and translation into `ResourceQuery` predicates using mapping metadata:
    - resolve `TAspect` → `AspectDefinitionId`
    - resolve property → facet/index field name
    - resolve operator semantics (keyword vs text vs range)

**DoD**
- Module can query resources by typed properties in a backend-agnostic way
- Index definitions derived from typed mapping where possible

## Epic 3.3 — Versioned schemas & upgrade pathway (Was Epic 4.3)
Resource Definitions (including aspect and facet attachments) should be versioned to support evolution.

**Deliverables**
- `ResourceDefinitionVersion` model:
    - immutable snapshots of definition + attachments
- Resources reference a definition version:
    - new resources default to latest definition version
- Upgrade API:
    - `UpgradeResourceToDefinitionVersion(resourceId, targetDefinitionVersionId)`
    - upgrade strategy hooks per aspect (optional)

**Semantics**
- Adding a facet: older resource versions remain valid; new definition version may supply defaults.
- Removing a facet: stored data may remain in historical payloads but becomes unmapped under newer definition versions.

**Facet type changes**
Recommended stance:
- Allow type change only via an explicit migration step.
- Compatibility matrix:
    - numeric widening (int → long → decimal/double) allowed with conversion
    - string → number/date requires explicit parser rules (culture-invariant) and may produce validation errors
    - incompatible changes fail validation and block activation until resolved

**DoD**
- Definition versions can be created and retrieved.
- Resources can be upgraded in a deterministic way.

---

# Phase 4 — Portability & Integration Hooks (Core) + Optional Recipe Modules

Aster needs *some* portability story (export/import), but a full “recipes” framework may be better as an **optional module** so Aster core stays focused.

## Epic 4.1 — Portability primitives (Aster core)
**Goal**
Define the minimal contracts needed for portable export/import of definitions/resources, without imposing an execution model.

**Deliverables**
- Portable package format contract (conceptual):
    - resource definitions (all versions or latest)
    - resources (latest, drafts, and/or active-by-channel)
    - references between resources (stable ids + remapping rules)
- `IPortabilityService` (or similar) for core operations:
    - `ExportAsync(PortabilityExportRequest)`
    - `ImportAsync(PortabilityImportRequest)`
- Deterministic ID remapping model:
    - preserve ids (same environment)
    - remap ids (cross environment)
    - conflict resolution policies

**DoD**
- Host can export/import without depending on a recipe execution framework.
- Clear merge/overwrite behaviors are documented.

## Epic 4.2 — Optional: Recipes framework (separate package)
**Candidate package**
- `Aster.Recipes` (optional add-on) OR separate product if it grows large.

**Scope**
- “Recipe” primitives:
    - `IRecipeStep`
    - `IRecipeExecutor`
- Built-in steps:
    - export/import resource definition versions
    - export/import resources
    - environment variables / parameter binding (optional)

**DoD**
- Aster core does **not** depend on this package.
- Hosts that want Orchard-style recipes can add it.

## Epic 4.3 — Host Hooks (UI + Behaviors) (Aster core)
**Deliverables**
- Events/pipeline:
    - `OnSaving`, `OnSaved`, `OnActivating`, `OnActivated`, `OnDeactivating`, `OnDeactivated`
- Host can attach behavior for auditing, permissions, etc.

**DoD**
- Aspects can attach behavior without requiring a UI framework

---

# Phase 5 — Multi-tenancy + Advanced Versioning + Policies

## Epic 5.1 — Tenant-aware definition scoping
**Deliverables**
- Tenant-scoped definitions
- Optional shared definitions (advanced)
- Tenant-aware query boundaries

**DoD**
- Multiple tenants can define different resource types safely

## Epic 5.2 — Policies
**Deliverables**
- Retention/archival policies
- Soft delete policy aspect
- Version pruning strategies

**DoD**
- Host can configure policies without custom code per resource type

---

# Phase 6 — Operational Hardening

## Epic 6.1 — Concurrency & conflicts
**Deliverables**
- Optimistic concurrency checks
- Conflict error model + optional merge strategy hooks

## Epic 6.2 — Perf & testing harness
**Deliverables**
- Benchmark harness
- Large data test suite (index rebuild, query latency)
- Migration test suite

## Epic 6.3 — Migration hardening & compatibility
**Deliverables**
- Migration test suite per provider:
    - upgrade path tests across multiple versions
    - idempotency tests
    - rollback tests (if provider supports down migrations)
- Migration policy options:
    - allow/deny automatic migration in production
    - locking strategy (prevent concurrent migration runs)
- Backward compatibility rules:
    - which schema versions are supported for runtime
    - index rebuild strategy when projection schema changes

**DoD**
- Migrations are safe under concurrent startup scenarios.
- Clear guidance for production deployments.

---

## 4) Proposed Package Layout (Draft)

- `Aster.Abstractions`
- `Aster.Definitions`
- `Aster.Runtime` (services, pipelines, versioning/state)
- `Aster.Querying` (query model + query service)
- `Aster.Indexing` (index abstractions + engine)
- `Aster.Recipes`
- `Aster.Persistence.<Backend>` (e.g., PostgresJsonb / SqliteJson / Mongo)
- `Aster.Hosting` (DI helpers)

---

## 5) Key Design Decisions to Capture Early (ADR candidates)

1. **Active vs Published semantics**
    - Prefer a general activation model with channels.
    - “Published” is a conventional channel, not the only one.

2. **Query Model vs IQueryable**
    - Prefer a backend-agnostic query model that can be translated.
    - `IQueryable` is fine *within a single backend*, but not as the public abstraction.

3. **Indexing approach**
    - Either commit to a single indexing engine early (simpler),
    - Or keep a pluggable index interface but ship one engine first.

4. **Document shape and growth strategy**
    - Resource versions are immutable.
    - Start with snapshots; add deltas + compaction later if needed.

5. **Typed aspect mapping**
    - Define how typed aspects map to document payload and index fields.

6. **Versioned schemas**
    - Resource definitions and attachments are versioned.
    - Resources reference a definition version and can be upgraded.

7. **Text vs NormalizedText (portable semantics)**
    - `Text` is for provider-specific full-text search behavior.
    - `NormalizedText` is for portable, deterministic substring matching.
