# Aster Studio — Admin Demo

A demo admin app ("Aster Studio") showcasing the Aster SDK as a headless CMS.
Single ASP.NET Core host serving Minimal APIs plus a built React SPA.

## Why this demo exists

Aster models content types (**resource definitions**) as compositions of
**horizontal aspects** — reusable field groups (Content, SEO, Publishable,
Taggable, Navigation) — where each aspect contains typed **facets** carrying
declarative validation/UI metadata (feature 038).

The point the demo makes: attach the *same* aspect to any content type and you
instantly get the same generated editing form, the same validation, and the
same behavior — no per-type UI code. Add a brand-new content type at runtime
in the Content Types screen and it is immediately fully editable.

Also on display: immutable resource versioning, per-channel activation
(preview/live), and field-level validation diagnostics flowing from
`FacetValueValidator` straight onto SPA form fields.

## Run

```bash
# 1. Build the SPA (once, or after frontend changes)
cd src/apps/Aster.Studio/ClientApp
npm install
npm run build

# 2. Run the host (serves API + built SPA on http://localhost:5210)
cd ../../../..
dotnet run --project src/apps/Aster.Studio
```

Open http://localhost:5210. A SQLite database is created at
`App_Data/aster-demo.db` (gitignored) and seeded on first run with five shared
aspects (`content`, `seo`, `publishable`, `taggable`, `navigation`) in the
aspect definition store, `article` and `page` content types that pin them, and
sample content. The seed registers `article` v2 after the articles exist: it
drops `navigation` and declares three policies (`archive-drafts`,
`soft-delete-stale-drafts`, `keep-latest-version`). The seeded articles stay on
v1, so they offer a schema upgrade, and `article-horizontal-aspects` carries
`navigation` forward when upgraded. Startup seeds an aspect only when the store doesn't have it, so
edits made in the Aspect Library survive restarts.

A second tenant, `acme`, gets the same five shared aspects (registered separately; portability matches aspect
versions by content, so both tenants' copies match) and its own content: a `page` type with only `content` and `seo`, a `product` type, and two
of each. Its `page` type and `page-home` share IDs with the default tenant's but not their content, so importing
those from the default tenant shows remapping.

The demo database is disposable. After pulling changes to the seed data, delete
`App_Data/aster-demo.db` and restart to reseed. Databases seeded before shared
aspects (#96) keep `article` and `page` with definition-local aspects: they
still work, but upgrades in the Aspect Library don't reach them.

### Frontend dev loop

```bash
dotnet run --project src/apps/Aster.Studio   # terminal 1
cd src/apps/Aster.Studio/ClientApp && npm run dev                # terminal 2
```

Vite proxies `/api` to http://localhost:5210.

### Smoke tests

Playwright drives the core flows end to end:
- create a content type from shared aspects
- create, edit and validate a resource, including server diagnostics on fields
- activate and deactivate on preview and live
- version history
- the tag filter

`e2e/portability.spec.ts` covers portability, tenancy and the query builder (#112):
- export a page from the default tenant, then preview and import it into Acme: strict mode refuses the divergent collisions, remapping imports them as `page__imported` and `<id>__imported`
- the tenant switcher isolating content, applying to the editor, and surviving a reload
- a query builder run, and an invalid query failing at `Filter.Operands[1].Operator` with `unsupported-comparison-operator`

`e2e/operations.spec.ts` covers the operations screens (#111):
- archive, the lifecycle filter, and restore, including a restore preview that went stale (`lifecycle-restore-marker-mismatch`)
- policy preview and archive application, including a stale candidate (`policy-application-stale-candidate`)
- version pruning; active versions are left out of the preview and reported as `policy-pruning-version-protected-active`
- a schema upgrade that carries an undeclared aspect forward
- historical activation and maintenance hints

```bash
cd src/apps/Aster.Studio/ClientApp
npm run build                       # the host serves the built SPA
npx playwright install chromium     # once
npm run test:e2e
```

`playwright.config.ts` starts the host itself on port 5299 (`STUDIO_E2E_PORT` overrides it), with a fresh SQLite
database in a temporary directory, so runs never touch `App_Data`. CI runs the suite on every PR
(`Aster Studio Playwright smoke`).

## Tour

| Screen | What it demonstrates |
|---|---|
| Dashboard | Counts and recent content across all definitions |
| Aspect Library | Shared aspects from `IAspectDefinitionStore`: create one, edit its facets and save a new version, then preview and apply the upgrade to the content types that pin an older version |
| Content Types | Definitions list; create a new type by pinning shared aspects at their latest version |
| Content list | Paged resources with version, channel and lifecycle badges; filter by tag and by lifecycle state (active, archived, soft-deleted) |
| Resource editor | **Centerpiece** — form generated 100% from facet metadata (uiHints, ordering, constraints); server diagnostics map to fields |
| Operations (resource editor) | Archive or soft-delete through `IResourceLifecycleMarkerService`; restore after a preview through `IResourceLifecycleRestoreService`; schema status from `IResourceSchemaVersionService` and an explicit upgrade that reports the carried-forward aspect keys |
| Version history | Immutable versions with maintenance hints from `IResourceVersionHistoryService` (protected or possible prune candidate) and the definition version each was saved against; activate/deactivate any version on preview/live, including historical activation that leaves latest alone |
| Policies (content type detail) | The definition's policy declarations and their validation; a preview as of any date; apply selected archive or soft-delete outcomes, or prune selected versions. The preview leaves out the versions pruning protects and lists them as diagnostics. Results show each candidate's status and diagnostic codes |
| Tenant switcher (header) | Scopes every screen and request to the default tenant or Acme; the choice persists across reloads |
| Portability | Export from the selected tenant through `IResourcePortabilityService`: content types with or without their resources, or selected resources, with all, latest or specific versions, and referenced resources if wanted; download the snapshot. Import it into the selected tenant: a preview shows planned counts, identity mappings (preserved, reused, remapped), reference rewrites and diagnostics with their codes, then the import writes all or nothing, strictly or remapping divergent content |
| Query Builder | Compose a `ResourceQuery`: content type, version scope and channel, lifecycle state, metadata and facet conditions with any operator (array and range included), aspect presence and reference targets, combined with and/or, plus sorting. `IResourceQueryValidator` checks every change and shows each failure's code and path on the condition it concerns; only a valid query runs. The provider's declared capabilities are listed alongside |

## API

Studio is the integration test bed for the [`Aster.AspNetCore`](../../hosting/Aster.AspNetCore/README.md) package (#110):

- **`/api/aster`:** `app.MapAster(...)` serves every resource and definition operation the SPA uses: read a resource or one of its versions, version history, create, update, activate, deactivate, read a definition, and create one. Errors are problem details; validation failures carry `diagnostics: [{ code, aspectKey, facetId, message }]`.
  - **Content types are create-only:** a filter rejects definition registration unless it names `expectedLatestVersion=0`, so no request can append a version to an existing type.
  - **The package's aspect group is not mapped:** its registration appends unconditionally, which would bypass Studio's aspect checks (#175).
- **Tenancy:** Studio registers an `ITenantScopeAccessor` that reads the `X-Aster-Tenant` header, which the SPA sends with every request. The package's endpoints and Studio's own resolve the tenant through it. Studio has no sign-in to check the header against, so it accepts only its seeded tenants (`default`, `acme`) and rejects others with `tenant-unknown`; a real host resolves the tenant from the caller's identity.
- **`/api/studio`:** only what the package doesn't provide:
  - **View aggregates:** definitions with resource counts, and a definition's resources with title, tags, active channels and a total count.
  - **The shared-aspect library:** versions and usage, create-only-if-missing, save based on a base version, and upgrade preview and apply.
  - **Operations (#111)** over the SDK's lifecycle, schema and policy services:
    - `resources/{id}/lifecycle` (read, archive or soft-delete), `restore-preview` and `restore`, each restore naming the state it expects to clear.
    - `resources/{id}/schema` and `schema-upgrade` (based on `baseVersion`).
    - `definitions/{id}/policies`, `policies/preview`, then `policies/apply` (archive and soft-delete) and `policies/prune`.
    - Results keep the SDK's per-candidate statuses and diagnostics, plus the summary from its summary helpers. A request that fails as a whole answers with problem details carrying `diagnostics: [{ code, message }]`.
    - Studio serializes enums as kebab-case names (`soft-deleted`, `prune-preview`), the package's version history included.
  - **Tenants (#112):** `tenants` lists the tenants the header may name.
  - **Portability (#112):** `portability/export`, then `portability/preview` and `portability/import`, each with the SDK result and its summary. The tenant always comes from the header: export reads it and import writes into it. Snapshots round-trip as Studio serializes them, kebab-case enums included.
  - **Query (#112):** `query/capabilities`, `query/validate` (the validator's failures with code, path and feature, plus its summary) and `query`, which validates first and runs only a valid query. The builder's conditions are the filter's operands, so a failure at `Filter.Operands[i]` belongs to condition `i`. Studio doesn't use the package's `POST /resources/query` here: it reports only the first failure, without a path, and has no reference-target condition.

`ClientApp/src/lib/api.ts` maps the package's models onto the SPA's view types. OpenAPI for the Studio endpoints is at `/openapi/v1.json` in Development.
