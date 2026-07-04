# Contract: Facet Validation and UI Metadata

This slice extends the core SDK definition model and its registration/save gates. All additions live in `Aster.Core`; the SQLite JSON provider only invokes the shared registration validator. No storage schema changes.

## Model Surface

- `FacetDefinition.Metadata` (`FacetMetadata?`, default `null`): optional metadata. `null` means the facet registers, validates, saves, exports, and imports exactly as before this feature.
- `FacetMetadata` (record): `Label`, `Description`, `DisplayOrder`, `DefaultValue`, `AllowedValues`, `MinLength`, `MaxLength`, `Minimum`, `Maximum`, `Pattern`, `UiHint` — all optional and independently declarable. `UiHint` is opaque and carried verbatim; the SDK never restricts or interprets it. `DefaultValue` is exposed to hosts as metadata only and is never injected into saved resource versions.
- `ResourceDefinitionBuilder.WithFacetMetadata(FacetMetadata)`: attaches metadata to the most recently added facet; throws `InvalidOperationException` when no facet has been added. Existing builder methods are unchanged.
- JSON contract: definitions without metadata serialize byte-identically to today (null metadata is omitted); partial metadata serializes only declared properties.

## Registration Contract

`IResourceDefinitionStore.RegisterDefinitionAsync` (in-memory and SQLite JSON implementations) MUST:

- accept any definition whose facets declare no metadata or valid metadata, with behavior otherwise unchanged (version auto-increment, tenant scoping, logging);
- reject invalid metadata by throwing `FacetMetadataValidationException` before any state change, carrying every violation across all aspects and facets;
- emit these stable diagnostic codes exactly:
  - `facet-metadata-range-invalid`
  - `facet-metadata-constraint-inapplicable`
  - `facet-metadata-allowed-values-empty`
  - `facet-metadata-allowed-values-duplicate`
  - `facet-metadata-allowed-value-type-mismatch`
  - `facet-metadata-default-value-invalid`
  - `facet-metadata-pattern-invalid`
- identify each violation by `AspectKey` (attachment key) and `FacetDefinitionId`;
- order diagnostics by aspect key, then facet identifier, then code, using ordinal string ordering;
- produce identical outcomes and codes on both providers for identical definitions.

Portable snapshot import is NOT a registration gate: it remains a lossless write of exact definition versions.

## Save Contract

`IResourceManager.CreateAsync` / `UpdateAsync` (`DefaultResourceManager`, `InMemoryResourceManager`) MUST:

- validate present facet values against declared metadata constraints before invoking before-save lifecycle hooks and before any write;
- reject violations by throwing `FacetValueValidationException` carrying every violation, with nothing persisted and no save lifecycle hooks invoked for that save;
- emit these stable diagnostic codes exactly:
  - `facet-value-not-allowed`
  - `facet-value-length-out-of-range`
  - `facet-value-out-of-range`
  - `facet-value-pattern-mismatch`
- identify each violation by `AspectKey` and `FacetDefinitionId`, ordered by aspect key, then facet identifier, then code (ordinal);
- skip constraints for absent/null values — missing-value handling remains governed exclusively by the existing `IsRequired` flag with no behavior change;
- treat boundary values (length or numeric value equal to a declared min/max) as valid;
- enforce all declared constraints together when several apply to one facet;
- evaluate patterns with standard .NET regex semantics under a bounded match timeout; an evaluation that exceeds the bound fails deterministically as `facet-value-pattern-mismatch`;
- leave saves for facets without validation metadata, and facets with display-only metadata, byte-identical in behavior to today;
- produce identical outcomes and codes against the in-memory store and the SQLite JSON provider.

## Portability Contract

- Export/import MUST round-trip all facet metadata losslessly through `PortableSnapshot` (definitions are carried as full `ResourceDefinition` records).
- Definitions without metadata MUST produce snapshot content unchanged from today.

## Determinism Contract

- Repeated validation of the same definition or resource version yields identical diagnostics in identical order across runs and providers.

## Non-Goals

- No product APIs beyond the core SDK.
- No storage schema changes (definitions stay JSON payloads in the SQLite JSON provider).
- No provider registries, runtime scanning, or automatic discovery.
- No public raw SQL or public `IQueryable<Resource>`.
- No schedulers or background workers.
- No localization of labels or messages.
- No client-side/UI framework code.
- No cross-facet or cross-aspect conditional validation.
- No async/remote validators.
- No save-time facet *type* enforcement (values that cannot be normalized to the declared type save exactly as today).
- No automatic default-value injection.
- No new dependencies.
