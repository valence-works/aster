# ADR 0003: Shared aspect definitions

- **Status:** Accepted
- **Date:** 2026-10-20
- **Program task:** [#91](https://github.com/valence-works/aster/issues/91) (epic [#64](https://github.com/valence-works/aster/issues/64))
- **Unblocks:** #92 (store), #93 (pin and resolve), #94 (upgrades), #95 (portability), #96 (Studio aspect library)

## Context

Aster's pitch is reusable horizontal aspects (SEO, Tags, Owner, Publishable) attached to any resource type. Today an `AspectDefinition` exists only as a snapshot embedded in `ResourceDefinition.AspectDefinitions`:

| Observation | Where | Consequence |
|---|---|---|
| `AspectDefinition` has `AspectDefinitionId`, `Id` and `Version`, but no store | `Models/Definitions/AspectDefinition.cs` | The identity fields suggest versioning, yet every builder call creates a fresh `Version = 1` with a new GUID. |
| The builder embeds a new aspect per attachment | `ResourceDefinitionBuilder.AddAspect` | Two content types that both "use SEO" hold unrelated copies; nothing tells you they are the same aspect. |
| Attachment keys are `{AspectDefinitionId}` or `{AspectDefinitionId}:{name}` | `ResourceDefinitionBuilder.AddAspect` | The aspect ID is part of every stored resource payload's key, so it can never be renamed or remapped without rewriting data. |
| Definition stores assign the next version and run `FacetMetadataValidator` themselves | `InMemoryResourceDefinitionStore`, `SqliteJsonResourceStore` | Registration is a provider primitive with no access to other stores. |
| Studio keeps an app-level `AspectTemplateCatalog` | `src/apps/Aster.AdminDemo` | The aspect library is a hard-coded list, not data. |

Embedded snapshots have a property worth keeping: a resource definition version is immutable and self-contained, so validation, rendering and schema export never need a second lookup, and persisted data does not depend on another table.

## Decision

### 1. Aspect definitions are independently stored, versioned and tenant-scoped

- **Identity:** `(TenantScope, AspectDefinitionId, Version)`, with a GUID `Id` per version. This is the same shape as resource definitions.
- **New store:** `IAspectDefinitionStore` (#92), with:
  - `RegisterAsync`: appends a version. The store assigns `latest + 1` and runs the facet metadata self-validation (`FacetMetadataValidator`), exactly like `IResourceDefinitionStore.RegisterDefinitionAsync`.
  - `RegisterIfLatestAsync` (#175): appends only when the latest version is still the expected one (`0`: the aspect must not exist), checked and written atomically, like `RegisterDefinitionIfLatestAsync`.
  - `GetLatestAsync`, `GetVersionAsync`, `ListAsync`, `ListVersionsAsync`.
- **Versions are immutable and append-only.** Registering identical content appends another version, as resource definitions do today. Hosts that seed aspects at startup compare with the latest version first. #96 adds that helper for Studio.
- **SQLite:** an additive `aspect_definitions` table, created idempotently under the 036/037 rules. Existing tables are unchanged.

*Rejected:*
- **A global aspect library shared by all tenants.** Every other entity in Aster is tenant-scoped (015), and cross-tenant reads would need a new authorization story. A host that wants the same library in every tenant registers it per tenant, or imports it with portability (§6).
- **Deduplicating identical registrations in the store.** It would make aspect stores behave differently from definition stores for no consumer benefit.

### 2. Resource definitions pin an aspect version and keep the resolved snapshot embedded

- **What attaching does:** attaching a shared aspect copies the stored version, unchanged (same `AspectDefinitionId`, `Id` and `Version`), into `ResourceDefinition.AspectDefinitions`.
- **The marker:** the copy carries a new `AspectDefinition.IsShared = true` flag. That flag *is* the pin: `(AspectDefinitionId, Version)` names the stored version the snapshot came from.
- **Definition-local aspects:** these are today's behaviour. They keep `IsShared = false` and their `Version` means nothing outside the resource definition.
- **Everything else is unchanged:** validation, rendering, queries, schema upgrades and persisted resource data keep reading the embedded snapshot. A resource definition version stays self-contained and immutable. Deleting or changing the stored aspect can never change an existing resource definition.

*Rejected:*
- **Resolving aspects by reference at read time** (storing only the pin). Every validation and render would need a second store read. A missing or tenant-mismatched aspect would break existing definitions, and the 012 schema-upgrade flow, which compares embedded snapshots, would need rewriting.
- **A separate `AspectPin` record beside the snapshot.** It is redundant with the snapshot's own identity fields, and the two could disagree.

### 3. Resolution is an explicit async build step, not a store concern

- **API:** `ResourceDefinitionBuilder.WithSharedAspect(aspectDefinitionId, version: null, name: null)` records a shared attachment. `null` means the latest version at build time.
- **Resolving:** shared attachments are resolved by `BuildAsync(IAspectDefinitionStore, TenantScope?)`, which reads the pinned (or latest) versions in that tenant and embeds them.
- **Guard:** the synchronous `Build()` throws when shared attachments are present, so an unresolved definition can never reach a store.
- **Failures are stable diagnostics:**
  - `aspect-definition-not-found`: the aspect or the pinned version does not exist in the tenant.
  - `aspect-name-required`: the stored aspect has `RequiresName = true` and the attachment is unnamed.
- **Resource definition stores stay provider primitives.** They receive fully resolved definitions and validate them as today.

*Rejected:*
- **Resolving inside `IResourceDefinitionStore.RegisterDefinitionAsync`.** Every provider would need the aspect store and would duplicate the resolution rules.
- **Persisting an unresolved placeholder and resolving later.** That breaks the self-contained guarantee of §2.

### 4. Named attachments pin exactly like unnamed ones

- **Attachment key:** `{AspectDefinitionId}:{name}`, as today. The embedded snapshot and its pin are identical for every attachment of the same version.
- **`RequiresName`:** the stored aspect's flag is authoritative; the attachment does not decide it.
  - An unnamed attachment of a `RequiresName` aspect is rejected.
  - A named attachment of an aspect that doesn't require a name is allowed.

### 5. Code-first typed aspects stay definition-local by default

- **`WithAspect<T>()` is unchanged.** It derives a definition-local aspect from `T` (ADR 0002 §8) and never touches the aspect store.
- **To share a typed aspect, a host opts in explicitly:**
  - `AspectDefinitionBuilder.FromType<T>()` builds an `AspectDefinition` from `T`, using the same derivation as `WithAspect<T>()`, for `IAspectDefinitionStore.RegisterAsync`.
  - `WithSharedAspect<T>(version: null, name: null)` attaches the stored aspect whose ID is `typeof(T).Name`.

*Rejected:* auto-registering typed aspects on `WithAspect<T>()`.
- The builder is synchronous and has no store or tenant.
- Every code deployment that touched a POCO would silently append a shared version and fan out to unrelated definitions. That contradicts the "explicit upgrades only" rule in §6.

### 6. Upgrades are explicit; nothing propagates automatically

Registering aspect version 2 changes nothing else. To move a resource definition onto it (#94):

1. **Preview.** `IAspectUpgradeService.PreviewAsync(aspectDefinitionId, targetVersion)` lists the resource definitions (latest versions) pinning an older version of the aspect, with a facet diff: added, removed, and type or constraint changes. It is deterministic and non-mutating.
2. **Apply.** `ApplyAsync(selection)` re-pins only the selected definitions, which registers **new resource definition versions** with the new snapshot embedded. Old definition versions and their resources are untouched.
3. **Upgrade resources.** Resources then move with the existing `IResourceSchemaVersionService` flow (012): append-only, with carry-forward diagnostics.

### 7. Portability carries aspect definitions; aspect versions match by content and are never remapped

- **Export:** `PortableSnapshot` gains `AspectDefinitions` (#95). Export includes the versions that the exported resource definitions pin, or every version when requested.
- **Import order:** aspect definitions, then resource definitions, then resources.
- **Identity is content (#195):** an aspect version matches the target's version with the same `(AspectDefinitionId, Version)` when their content is equal: `RequiresName` and each facet's ID, type, requiredness, version and metadata. The version `Id` and the facet `Id`s are generated per registration and ignored, so a tenant that registered the same aspect independently matches.
- **Definitions too (#221):** resource definitions follow the same rule. A definition matches the target's definition with the same `(DefinitionId, Version)` when their content is equal, ignoring the definition `Id` and the `Id`s of its aspects and facets; its pins are compared after embedding the target's copy. A match is reused in every collision mode, and the target keeps its copy. A definition with different content still collides, and only definitions are remapped under `RemapDivergent`.
- **Collisions:** a matching version is reused, and the imported definitions' pins are rewritten to embed the target's copy. A version with different content fails as `divergent-identity-collision` in every collision mode, `RemapDivergent` included: a shared aspect is never remapped.
  - Renaming or renumbering it would fork the tenant's vocabulary and orphan every pin to it, and the `AspectDefinitionId` is part of resource payload keys (see Context).
  - Embedded snapshots keep resources valid whatever happens to the pin.
- **Legacy and partial snapshots:** a snapshot without aspect definitions imports exactly as before. A shared pin that resolves neither in the snapshot nor in the target tenant is reported as `unresolved-aspect-pin`. It is a warning, not an error, because the embedded snapshot still makes the definition valid; only upgrade previews lose the link.

## Consequences

- **Backward compatibility:**
  - Existing definitions have `IsShared = false` (the default), so they load, validate and behave identically.
  - Definition JSON gains one boolean. Older readers ignore it, and snapshots without it deserialize to `false`.
  - No resource data is migrated.
- **New public surface:** `IAspectDefinitionStore`, `AspectDefinition.IsShared`, `WithSharedAspect` / `BuildAsync`, `AspectDefinitionBuilder`, `IAspectUpgradeService`, `PortableSnapshot.AspectDefinitions`, and the diagnostics `aspect-definition-not-found`, `aspect-name-required` and `unresolved-aspect-pin`.
- **Studio (#96):** `AspectTemplateCatalog` is replaced by aspect definitions seeded into the store. The aspect library becomes editable, and content types attach aspects by pin.
- **What stays manual:** a shared aspect's facet IDs and types still constrain stored payloads through the embedded snapshot. Changing a facet type in aspect v2 is an ordinary schema change, and resources move only through the explicit upgrade flow.
