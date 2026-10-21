# Concepts & Terminology

Aster is built around a small, precise domain model. Understanding these terms is essential to using the SDK effectively.

---

## The Core Hierarchy

```
ResourceDefinition   ←  schema / "type"
    └─ AspectDefinition[]   ←  reusable "parts" attached to the definition
           └─ FacetDefinition[]   ←  individual typed fields within an aspect

Resource             ←  versioned instance of a ResourceDefinition
    └─ AspectInstance[]     ←  per-version data for each attached aspect
           └─ FacetValue[]  ←  actual field values
```

---

## Term Reference

### Resource Definition

The **schema** for a resource type. Examples: `Product`, `WorkflowDefinition`, `Agent`, `Secret`.

- Identified by a logical `DefinitionId` string (e.g. `"Product"`).
- Each call to `RegisterDefinitionAsync` creates a new **immutable version** — the old version is never mutated.
- `Version` is auto-incremented; existing resources continue to reference the definition version they were created against.
- `IsSingleton = true` restricts creation to a single instance.

### Resource

A **versioned instance** of a Resource Definition.

- `ResourceId` — the stable logical identifier, shared across all versions.
- `Id` — a GUID uniquely identifying this specific version snapshot.
- `Version` — ordinal (1, 2, 3…); incremented on every `UpdateAsync` call.
- **Status is derived** — no explicit status field. A version with no activation entry is implicitly a *draft*; presence in a channel's active set makes it *active*.

> The old separate `ResourceVersion` model has been merged into `Resource`. `ResourceId` = logical identity; `Id` = version snapshot identity.

### Identifiers

`ResourceId`, `DefinitionId`, `AspectDefinitionId`, activation channels and tenant IDs are opaque, case-sensitive, exact-match strings. They must not be blank and must not start or end with whitespace; whitespace inside one (`"product one"`) is fine.

Providers compare padded strings differently (SQL Server ignores trailing spaces, SQLite and PostgreSQL do not), so core rejects a padded identifier wherever a record is written for the first time: `TenantScope.FromTenantId`, `CreateAsync` (definition ID, caller-supplied resource ID, tenant ID), definition and aspect definition registration, `ActivateAsync` (channel), and snapshot import. The failure is an `InvalidIdentifierException` (an `ArgumentException`) with code `identifier-whitespace`, or an `identifier-whitespace` diagnostic for snapshots.

Lookups of existing records (reads, `UpdateAsync`, `DeactivateAsync`, version history, lifecycle markers, policy application, export, query filters) are not checked and never trim, so data stored under a padded ID before the rule existed stays reachable under its exact ID. See [Exception Reference](Exception-Reference#invalididentifierexception).

### Aspect Definition

A reusable "part" that can be attached to any Resource Definition. Think of it like a mixin or a trait.

Examples: `TitleAspect`, `PriceAspect`, `TagsAspect`, `OwnerAspect`, `AuditAspect`.

- Has its own logical `AspectDefinitionId` (derived from `typeof(T).Name` when using the builder).
- Can be attached **unnamed** (at most once per definition) or **named** (multiple times, distinguished by a `Name` discriminator).

### Named Aspects

The same Aspect Definition can be attached to a Resource Definition more than once by giving each attachment a distinct **name**.

Attachment key scheme:
- Unnamed: `"TitleAspect"` (the `AspectDefinitionId`)
- Named: `"TagsAspect:Categories"` (composite `"{AspectDefinitionId}:{Name}"`)

Example: attach `TagsAspect` twice — once as `"Categories"` and once as `"Badges"`.

> Named aspects significantly increase query complexity. Their use in querying may be restricted in early phases.

### Facet Definition

A **typed field** declared inside an Aspect Definition. Examples:

- `TitleAspect.Title : string`
- `PriceAspect.Amount : decimal`
- `PriceAspect.Currency : string`

At the code level, facets are the properties of your C# POCO aspect types.

A facet value is read by its facet ID or, when the aspect payload has no such key, by the camel-case form of the ID (first letter lower-cased: `title` for `Title`), which is how typed aspects are stored. Two facets of one aspect whose IDs differ only in the case of their first letter (`Name` and `name`) would therefore read each other's value, so definition and aspect definition registration refuse them with a `FacetMetadataValidationException` diagnostic `facet-metadata-id-case-collision` (#240). `Name` and `NAME`, or `Name` and `name` in different aspects, are fine. Definitions stored before the rule existed keep loading; registering a new version of one requires renaming a facet.

### Facet Value

The actual stored value of a facet on a specific aspect instance attached to a specific resource version.

### Aspect Instance

The per-resource-version data for one attached aspect. Stored internally as a dictionary of facet values (`IReadOnlyDictionary<string, object>`), but accessed via typed binders (`ITypedAspectBinder` / `ITypedFacetBinder`).

### Activation Channel

A **named delivery context** for resource versions. Examples: `"Published"`, `"Staging"`, `"Preview"`, `"Mobile"`.

- A version can be active in **zero or more** channels simultaneously.
- A channel can hold **multiple active versions** simultaneously (`allowMultipleActive = true`) or enforce single-active semantics (default).
- "Published" is just a conventional channel name — no special first-class status in the model.

### Draft

A resource version with no activation entries. Implicitly created by `CreateAsync` and `UpdateAsync`.

---

## Universal Versioning Pattern

All three definition types follow the same pattern:

| Type | Logical ID | Snapshot ID |
|---|---|---|
| `ResourceDefinition` | `DefinitionId` | `Id` |
| `AspectDefinition` | `AspectDefinitionId` | `Id` |
| `FacetDefinition` | `FacetDefinitionId` | `Id` |
| `Resource` | `ResourceId` | `Id` |

This consistency simplifies working across the model — the same identity rules apply everywhere.

---

## Cross-Cutting Aspects (use-case examples)

One of the key motivations for Aster is making horizontal capabilities **reusable** and **attachable** without hand-modelling them for every entity type:

| Aspect | Purpose |
|---|---|
| `Tags` | Add tagging to any resource type |
| `Owner` | Link to a user/subject; drives filtering and permission defaults |
| `RBAC / ACL` | Per-resource access control |
| `Auditing` | Created/modified timestamps + actor identifiers |
| `Scheduling` | Activate-at / deactivate-at windows |
| `Deployable` | Associate resources with deployment targets/environments |
| `Soft-delete` | Lifecycle management policies |
| `Localization` | Per-culture variants (planned for later phases) |

