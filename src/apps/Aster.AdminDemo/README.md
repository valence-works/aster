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
dotnet run --project src/apps/Aster.AdminDemo -p:NuGetAudit=false
```

> `-p:NuGetAudit=false` works around a pre-existing NU1903 advisory on a
> transitive SQLitePCLRaw package; no packages are pinned differently here.

Open http://localhost:5210. A SQLite database is created at
`App_Data/aster-demo.db` (gitignored) and seeded on first run with `article`
and `page` content types plus sample content.

### Frontend dev loop

```bash
dotnet run --project src/apps/Aster.AdminDemo -p:NuGetAudit=false   # terminal 1
cd src/apps/Aster.AdminDemo/ClientApp && npm run dev                # terminal 2
```

Vite proxies `/api` to http://localhost:5210.

## Tour

| Screen | What it demonstrates |
|---|---|
| Dashboard | Counts and recent content across all definitions |
| Aspect Library | The five horizontal aspect templates and their facet metadata |
| Content Types | Definitions list; create a new type by composing aspect templates |
| Content list | Paged resources with version + channel badges |
| Resource editor | **Centerpiece** — form generated 100% from facet metadata (uiHints, ordering, constraints); server diagnostics map to fields |
| Version history | Immutable versions; activate/deactivate any version on preview/live |

## API

Minimal APIs under `/api` (OpenAPI at `/openapi/v1.json` in Development):
aspect-templates, definitions (list/detail/create), resources
(list+paging/detail/versions/create/update/activate/deactivate).
Validation failures return `400 { diagnostics: [{ code, aspectKey, facetId, message }] }`.
