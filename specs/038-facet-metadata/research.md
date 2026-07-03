# Research: Facet Validation and UI Metadata

## Decision: Nest Metadata as One Optional Record on FacetDefinition

**Decision**: Add a single nullable `Metadata` property of a new `FacetMetadata` record to the existing `FacetDefinition` record, rather than adding ten flat optional properties.

**Rationale**: `FacetDefinition` currently carries only identity, version, type, and the required flag. A single nullable record keeps "no metadata" trivially detectable (`Metadata is null`), keeps identity/versioning/type semantics untouched (FR-002, spec Key Entities), and gives the validators one cohesive input. All metadata properties inside the record are themselves optional (`init` + nullable) so partial declarations (US1 scenario 3) preserve exactly what was declared.

**Alternatives considered**:

- Flat optional properties on `FacetDefinition`: rejected because "declared no metadata" becomes a ten-way null check and the model reads worse.
- A separate metadata store keyed by facet id: rejected as a parallel schema — the exact anti-pattern the feature eliminates, and it would break lossless snapshot round-trips for free.

## Decision: Suppress Null Metadata in JSON to Keep No-Metadata Payloads Byte-Identical

**Decision**: Annotate the new `Metadata` property (and nullable properties inside `FacetMetadata`) with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`.

**Rationale**: Both the SQLite JSON provider (`SqliteJsonResourceStore.Serialize`, `JsonSerializerDefaults.Web`) and any host serializing `PortableSnapshot` would otherwise emit `"metadata": null` for every existing facet, changing persisted payloads and snapshot content for definitions that declare nothing — violating FR-019 and SC-004. With the annotation, a definition without metadata serializes exactly as today, and partial metadata serializes only declared properties (US1 scenario 3).

**Alternatives considered**:

- Global `DefaultIgnoreCondition = WhenWritingNull` on provider serializer options: rejected because it changes serialization of *all* existing nullable properties (e.g., `AspectDefinition.Schema`), which is a wider behavior change than this slice allows.
- Accept `"metadata": null` in payloads: rejected — deserialization would tolerate it, but snapshot content for unchanged definitions would differ from today, violating FR-019.

## Decision: Registration Validation via a Shared Static Core Validator Called by Both Stores

**Decision**: Add `FacetMetadataValidator` (static class in `Aster.Core.Services`, following the `TenantScopeResolver` precedent) that validates a whole `ResourceDefinition` and throws `FacetMetadataValidationException` carrying an ordered list of `FacetMetadataDiagnostic`. Invoke it at the top of `RegisterDefinitionAsync` in both `InMemoryResourceDefinitionStore` and `SqliteJsonResourceStore` (tenant-scoped overloads, which the default overloads already delegate to).

**Rationale**: No registration-time definition validation currently exists; registration goes straight through `IResourceDefinitionStore.RegisterDefinitionAsync` in each provider. Placing the logic in one shared static helper and calling it from both stores guarantees identical outcomes and codes (FR-020) by construction, without introducing a new interface, DI seam, or provider registry (FR-021, constitution 20). Throwing a code-carrying exception matches the established registration/save failure channel (`TenantScopeException`, `DuplicateAspectAttachmentException`, `ResourceSchemaUpgradeException` all throw with stable codes).

**Alternatives considered**:

- New `IFacetMetadataValidator` interface + DI registration: rejected — no test seam or provider boundary needs it; stores are constructed without such collaborators today and adding constructor parameters would churn both providers for no present value.
- Validating only in `ResourceDefinitionBuilder.Build()`: rejected — definitions can be constructed directly as records and imported/registered without the builder; registration is the enforcement gate the spec names.
- Decorating `IResourceDefinitionStore`: rejected as indirection (constitution 18/20) and easy to bypass by resolving the concrete store.

## Decision: Portable Import Stays Lossless and Does Not Re-Validate Metadata

**Decision**: `ResourcePortabilityService` import and the stores' internal `ImportDefinitionVersion` paths do not invoke `FacetMetadataValidator`.

**Rationale**: FR-019 requires lossless round-trips of exact definition versions; imported definitions were validated when originally registered. Re-validating on import would introduce a new failure mode into the import contract that the spec does not define, and could reject historically valid snapshots if rules ever tighten. Registration (`RegisterDefinitionAsync`) remains the sole self-validation gate, exactly as the spec's US2 states.

**Alternatives considered**:

- Validate on import and fail with the same codes: rejected — spec scopes rejection to registration; import failure semantics are owned by the portability feature (specs 013/028) and unchanged here.

## Decision: Save-Time Validation in Both Resource Managers via a Shared Static Validator

**Decision**: Add `FacetValueValidator` (static class in `Aster.Core.Services`) that, given the resolved `ResourceDefinition` and the resource's aspect payloads, returns ordered `FacetValueDiagnostic`s; `DefaultResourceManager.CreateAsync/UpdateAsync` and `InMemoryResourceManager.CreateAsync/UpdateAsync` invoke it after resolving the definition and before the before-save lifecycle hook and any write, throwing `FacetValueValidationException` when violations exist.

**Rationale**: `DefaultResourceManager` is the registered `IResourceManager` for both the in-memory and SQLite configurations (both providers register their stores only as reader/writer/definition store), so validating there covers the provider-parity matrix (FR-020). `InMemoryResourceManager` also implements `IResourceManager` and is exercised directly by existing tests, so it gets the same one-line invocation to avoid divergent save semantics inside the SDK. Validating before hooks and writes guarantees "nothing is persisted" (FR-016) and avoids invoking before-save hooks for saves that can never succeed. Both managers already resolve the definition on the create path; the update path resolves it the same way (definition absent ⇒ no metadata ⇒ no checks, unchanged from today).

**Alternatives considered**:

- Validate inside `IResourceVersionWriter.SaveVersionAsync` implementations: rejected — writers don't have the definition in hand (would need extra lookups per provider, duplicating logic and risking divergence), and portability import writes exact historical versions that must stay lossless.
- A before-save lifecycle hook performing validation: rejected — hooks are host extension points with best-effort semantics; core data-quality guarantees must not depend on hook registration (explicitness, FR-021's "no hidden validation" inverse).

## Decision: Resolve Facet Values with the Same Semantics as IndexProjectionEvaluator

**Decision**: Add an internal static helper `AspectFacetValueResolver` in `Aster.Core.Services` that resolves a facet value from an aspect payload supporting the three established shapes — `IDictionary` entries, `JsonElement` properties, and POCO properties (case-insensitive name match) — mirroring `IndexProjectionEvaluator.TryResolveFacetValue`, including `JsonElement` → CLR conversion. `FacetValueValidator` treats an unresolvable or null value as "absent" (constraints skipped; required-flag semantics untouched, FR-015).

**Rationale**: `IndexProjectionEvaluator` already defines how the SDK maps `(aspectKey, facetKey)` onto the heterogeneous `Resource.Aspects` payloads (raw dictionaries, deserialized JSON, typed POCOs). Reusing those exact semantics keeps facet identity resolution consistent across features and providers — the SQLite provider round-trips aspects through JSON (values arrive as `JsonElement`), while in-memory keeps CLR objects; one resolver normalizes both, which is precisely what provider parity requires.

**Alternatives considered**:

- Refactor `IndexProjectionEvaluator` to expose its private helpers publicly: rejected — widening a shipped type's surface for internal reuse is unnecessary; an internal helper keeps the change additive and deletable (a follow-up may consolidate both onto the helper without public surface change).
- Validate only dictionary-shaped aspects: rejected — typed POCO aspects are a first-class authoring path (`WithTypedAspect<T>`), and skipping them would make enforcement depend on the caller's payload shape.

## Decision: Typed Comparison Semantics per Declared Facet Type

**Decision**: Normalize metadata values (default, allowed values) and saved values to the facet's declared type before comparison:

- `string`: ordinal comparison; length constraints use `string.Length` (UTF-16 code units); pattern applies to the string value.
- `int`: normalize to `long` (accepting boxed integral types and integral `JsonElement` numbers); range bounds and allowed values must be whole numbers; comparisons are integer comparisons.
- `decimal`: normalize to `decimal`; comparisons are decimal comparisons.
- `bool`: only label/description/display order/UI hint/default apply (allowed values, length, range, pattern are constraint-inapplicable per FR-005; a default must normalize to a boolean).
- `datetime`: normalize to `DateTime` (accepting `DateTime`/`DateTimeOffset` instances and ISO 8601 strings, round-trip semantics); allowed values and default compare by normalized instant; range/length/pattern are constraint-inapplicable (spec Assumptions).

Range bounds (`Minimum`/`Maximum`) are declared as `decimal?`; for `int` facets registration additionally rejects non-whole bounds as `facet-metadata-range-invalid` (a fractional bound cannot describe an integer range coherently), and value comparison converts the value to `decimal` (exact for `long`) so one comparison path serves both numeric types while honoring declared-type normalization first.

**Rationale**: The spec's edge case mandates declared-type semantics (integer comparison for int facets, decimal for decimal facets). `decimal` represents every `long` exactly, so decimal-typed bounds with integral normalization satisfy both without a parallel bound representation. Ordinal string comparison matches the repository-wide `StringComparison.Ordinal` convention and the spec's ordinal-ordering rule.

**Alternatives considered**:

- `object` bounds matching the facet type: rejected — JSON round-trips would turn them into `JsonElement` anyway, forcing the same normalization while making the model shape vaguer.
- `double` bounds: rejected — binary floating point cannot represent decimal bounds exactly, breaking boundary-value guarantees (edge case: value equal to min/max MUST pass).

## Decision: Allowed Values and Default Value Modeled as JSON-Tolerant Objects

**Decision**: `AllowedValues` is `IReadOnlyList<object>?` and `DefaultValue` is `object?`; the validators normalize each entry to the declared type (handling CLR primitives and `JsonElement` after JSON round-trips). Registration rejects entries that fail normalization with `facet-metadata-allowed-value-type-mismatch`, an empty list with `facet-metadata-allowed-values-empty`, and duplicates *after normalization* (ordinal for strings, value equality for numerics/datetimes/…) with `facet-metadata-allowed-values-duplicate`. The default value is checked against type and every declared constraint (`facet-metadata-default-value-invalid`), and is never injected into saved versions (FR-018).

**Rationale**: Values must survive `System.Text.Json` round-trips through the SQLite payloads and portable snapshots without a schema; `object` plus normalization is exactly how aspect payload values already behave in this codebase. Normalizing before duplicate detection prevents `1` and `1.0` (or `"2026-01-01"` in two spellings) from masquerading as distinct allowed values.

**Alternatives considered**:

- Strings-only allowed values: rejected — the spec requires allowed values for string, int, decimal, and datetime facets with type-mismatch diagnostics, which implies typed entries.
- A generic `FacetMetadata<T>`: rejected — `FacetDefinition.Type` is a runtime string descriptor; compile-time generics can't follow it and would fracture the definition model.

## Decision: Bounded, Deterministic Regex Evaluation

**Decision**: At registration, validate pattern well-formedness by constructing `new Regex(pattern)` and mapping `ArgumentException` to `facet-metadata-pattern-invalid`. At save time, evaluate with `Regex.IsMatch(value, pattern, RegexOptions.None, matchTimeout)` using a small constant timeout (1 second), and treat `RegexMatchTimeoutException` as a deterministic `facet-value-pattern-mismatch` failure. Matching uses standard .NET semantics (unanchored unless the host anchors the pattern).

**Rationale**: The spec requires well-formedness checks at registration and save-time evaluation that can never hang a save; a value whose evaluation exceeds the bound "fails the value deterministically" (edge case). `Regex` with an explicit `matchTimeout` is the platform mechanism for exactly this; no new dependency is needed. Standard unanchored `IsMatch` semantics match the Assumption that hosts author platform-syntax patterns and own their anchoring.

**Alternatives considered**:

- `RegexOptions.NonBacktracking`: rejected — not available on all supported TFMs' full feature set and it silently rejects some valid pattern constructs, which would make registration acceptance differ by target framework.
- Cached compiled regexes keyed by pattern: rejected for this slice — a cache is an optimization with lifetime questions; validation constructs the regex per evaluation, which is simple, correct, and deletable (revisit only with demonstrated need).

## Decision: Values That Cannot Be Normalized to the Declared Type Are Not Newly Rejected

**Decision**: If a present saved value cannot be normalized to the facet's declared type, save-time metadata constraint checks are skipped for that value and the save proceeds exactly as today.

**Rationale**: Today no save-time facet type enforcement exists — mistyped values already persist. The spec's diagnostic contract defines no `facet-value-type-mismatch` code, and FR-021 forbids expanding the surface beyond the defined codes; failing such values under, say, `facet-value-not-allowed` would misreport the actual problem and change behavior for hosts that (today, legally) store shape-divergent payloads. Type enforcement is a candidate future slice.

**Alternatives considered**:

- Treat unnormalizable values as violating every declared constraint: rejected — misleading diagnostics and a hidden behavior change beyond the spec.
- Add a new type-mismatch diagnostic code: rejected — outside the spec's stable code set (FR-021, SC-002/SC-003 enumerate the covered codes).

## Decision: Deterministic Diagnostic Ordering and Identity

**Decision**: Both validators emit diagnostics carrying `Code`, `Message`, `AspectKey` (the attachment key from `ResourceDefinition.AspectDefinitions` / `Resource.Aspects`), and `FacetDefinitionId`, sorted with `StringComparer.Ordinal` by aspect key, then facet definition id, then code. All violations across all aspects and facets are collected before throwing (FR-010, FR-016).

**Rationale**: The attachment key (e.g., `PriceAspect` or `PriceAspect:Sale`) is the identifier hosts use to address aspects on both definitions and resources, so it is the right "offending aspect" identity for mapping failures to form fields (US3). Ordinal ordering matches the spec's explicit rule and the repository's pervasive `StringComparison.Ordinal` convention, and makes repeated validation output byte-stable (SC-007).

**Alternatives considered**:

- Declaration order: rejected — dictionary iteration order is not a contract, and the spec explicitly prescribes ordinal key ordering.

## Decision: Builder Ergonomics via WithFacetMetadata on the Last Facet

**Decision**: Add `ResourceDefinitionBuilder.WithFacetMetadata(FacetMetadata metadata)` that attaches metadata to the most recently added facet of the most recently added aspect (throwing `InvalidOperationException` when no facet exists), mirroring how `WithTypedFacet<T>` targets the last aspect. Hosts can equally set `Metadata` directly when constructing `FacetDefinition` records.

**Rationale**: The builder is the documented authoring path; a single chaining method keeps declaration adjacent to `WithTypedFacet<T>()` without changing any existing method signature (additive-only).

**Alternatives considered**:

- Optional parameter on `WithTypedFacet<T>(FacetMetadata?)`: viable but rejected as slightly worse chaining ergonomics and a signature change to an existing method; a separate method keeps the diff purely additive.
- A nested facet-builder DSL: rejected as speculative infrastructure (constitution 16/20) — `FacetMetadata` is a small record hosts can write inline.
