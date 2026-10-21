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
cd src/apps/Aster.AdminDemo/ClientApp
npm install
npm run build

# 2. Run the host (serves API + built SPA on http://localhost:5210)
cd ../../../..
dotnet run --project src/apps/Aster.AdminDemo
```

Open http://localhost:5210. A SQLite database is created at
`App_Data/aster-demo.db` (gitignored) and seeded on first run with five shared
aspects (`content`, `seo`, `publishable`, `taggable`, `navigation`) in the
aspect definition store, `article` and `page` content types that pin them, and
sample content. Startup seeds an aspect only when the store doesn't have it, so
edits made in the Aspect Library survive restarts.

The demo database is disposable. After pulling changes to the seed data, delete
`App_Data/aster-demo.db` and restart to reseed. Databases seeded before shared
aspects (#96) keep `article` and `page` with definition-local aspects: they
still work, but upgrades in the Aspect Library don't reach them.

### Frontend dev loop

```bash
dotnet run --project src/apps/Aster.AdminDemo   # terminal 1
cd src/apps/Aster.AdminDemo/ClientApp && npm run dev                # terminal 2
```

Vite proxies `/api` to http://localhost:5210.

## Tour

| Screen | What it demonstrates |
|---|---|
| Dashboard | Counts and recent content across all definitions |
| Aspect Library | Shared aspects from `IAspectDefinitionStore`: create one, edit its facets and save a new version, then preview and apply the upgrade to the content types that pin an older version |
| Content Types | Definitions list; create a new type by pinning shared aspects at their latest version |
| Content list | Paged resources with version + channel badges |
| Resource editor | **Centerpiece** — form generated 100% from facet metadata (uiHints, ordering, constraints); server diagnostics map to fields |
| Version history | Immutable versions; activate/deactivate any version on preview/live |

## API

Minimal APIs under `/api` (OpenAPI at `/openapi/v1.json` in Development):
aspects (list/detail/create/save-as-new-version, upgrade preview/apply),
definitions (list/detail/create), resources
(list+paging/detail/versions/create/update/activate/deactivate).
Validation failures return `400 { diagnostics: [{ code, aspectKey, facetId, message }] }`.
