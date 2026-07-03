# Feature Specification: Facet Validation and UI Metadata

**Feature Branch**: `038-facet-metadata`  
**Created**: 2026-07-04  
**Status**: Draft  
**Input**: User description: "Extend Aster's facet definition model with optional, declarative validation and UI metadata so host applications can generate editing forms and validate facet values without maintaining out-of-band schemas."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Declare Facet Metadata (Priority: P1)

As a host developer, I want to declare optional display and validation metadata directly on a facet definition — label, description, display order, default value, allowed values, length constraints, numeric range, pattern, and a UI hint — so my application can generate editing forms from the definition itself instead of maintaining a parallel schema.

**Why this priority**: Declarative metadata on the definition is the foundation of the feature. Even without enforcement, hosts can immediately read the metadata to drive form generation, which is the primary value proposition.

**Independent Test**: Build a resource definition whose facets carry metadata, register it, read it back through the definition store, and verify every metadata property is preserved exactly. Register a definition without metadata and verify it behaves identically to today.

**Acceptance Scenarios**:

1. **Given** a facet definition with a display label, description, display order, default value, allowed values, length constraints, numeric range, pattern, and UI hint, **When** the definition is registered and read back, **Then** all metadata values are returned exactly as declared.
2. **Given** an existing facet definition declared without any metadata, **When** it is registered and used, **Then** registration, save, query, and portability behavior are unchanged from today.
3. **Given** a facet definition with only a subset of metadata (e.g., only a label and a UI hint), **When** it is registered and read back, **Then** the declared properties are preserved and all undeclared properties are absent.

---

### User Story 2 - Reject Invalid Metadata at Registration (Priority: P2)

As a host developer, I want invalid metadata combinations rejected when a definition is registered, with stable, deterministic diagnostic codes, so misconfigured definitions fail fast at authoring time instead of producing confusing behavior at save time.

**Why this priority**: Self-validation protects the integrity of every downstream consumer (form generation, value validation, portability). Without it, invalid metadata silently corrupts the editing and validation experience.

**Independent Test**: Attempt to register definitions with each invalid metadata combination and verify each is rejected with its expected stable diagnostic code; register a definition with valid metadata and verify it succeeds.

**Acceptance Scenarios**:

1. **Given** a facet whose minimum length, or numeric minimum, is greater than its declared maximum, **When** the definition is registered, **Then** registration fails with the stable range-invalid diagnostic code.
2. **Given** a facet whose declared type does not support a declared constraint (e.g., length constraints on a numeric facet, numeric range on a string facet, pattern on a boolean facet), **When** the definition is registered, **Then** registration fails with the stable constraint-inapplicable diagnostic code.
3. **Given** a facet with an allowed-values list that is empty, **When** the definition is registered, **Then** registration fails with the stable allowed-values-empty diagnostic code.
4. **Given** a facet with an allowed-values list containing a value that does not match the facet's declared type, **When** the definition is registered, **Then** registration fails with the stable allowed-value-type-mismatch diagnostic code.
5. **Given** a facet whose default value violates its own declared constraints (type, allowed values, length, range, or pattern), **When** the definition is registered, **Then** registration fails with the stable default-value-invalid diagnostic code.
6. **Given** a facet with a regular-expression pattern that is not a valid pattern, **When** the definition is registered, **Then** registration fails with the stable pattern-invalid diagnostic code.
7. **Given** a definition with multiple facets carrying multiple metadata errors, **When** the definition is registered, **Then** all errors are reported together in a deterministic order, each identifying the offending aspect and facet.

---

### User Story 3 - Enforce Metadata Constraints on Save (Priority: P3)

As a host developer, I want facet values validated against the declared metadata constraints when a resource version is saved, with stable, deterministic diagnostic codes on violation, so invalid data cannot enter the store and my application can map failures to form fields.

**Why this priority**: Enforcement turns the declared metadata into a real data-quality guarantee. It depends on User Stories 1 and 2 existing, and delivers the second half of the "no out-of-band schemas" promise.

**Independent Test**: Save resource versions whose facet values violate each constraint kind and verify each save fails with the expected stable diagnostic code; save conforming values and verify the save succeeds; save values for facets without metadata and verify behavior is unchanged from today.

**Acceptance Scenarios**:

1. **Given** a facet with an allowed-values list, **When** a resource version is saved with a value outside that list, **Then** the save fails with the stable value-not-allowed diagnostic code.
2. **Given** a string facet with length constraints, **When** a value shorter than the minimum or longer than the maximum is saved, **Then** the save fails with the stable length-out-of-range diagnostic code.
3. **Given** a numeric facet with range constraints, **When** a value below the minimum or above the maximum is saved, **Then** the save fails with the stable value-out-of-range diagnostic code.
4. **Given** a string facet with a pattern, **When** a value that does not match the pattern is saved, **Then** the save fails with the stable pattern-mismatch diagnostic code.
5. **Given** facet values that satisfy all declared constraints, **When** the resource version is saved, **Then** the save succeeds and persists exactly as today.
6. **Given** a facet definition without validation metadata, **When** any value is saved for that facet, **Then** save behavior is unchanged from today.
7. **Given** a resource version with multiple violating facet values, **When** the save is attempted, **Then** all violations are reported together in a deterministic order, each identifying the offending aspect and facet, and nothing is persisted.

---

### User Story 4 - Portable and Provider-Neutral Metadata (Priority: P4)

As a host developer, I want facet metadata to round-trip losslessly through portable snapshot export/import and to persist and behave identically through the in-memory store and the SQLite JSON provider, so environments stay consistent without provider-specific handling.

**Why this priority**: Portability and provider parity are established guarantees for every definition capability in Aster. Metadata that decays across export/import or diverges between providers would undermine the feature everywhere.

**Independent Test**: Export a definition with full metadata to a portable snapshot, import it into a fresh store, and verify byte-for-byte metadata equivalence; run the registration and save-validation scenarios against both the in-memory store and the SQLite JSON provider and verify identical outcomes and diagnostic codes.

**Acceptance Scenarios**:

1. **Given** a registered definition whose facets carry full metadata, **When** it is exported to a portable snapshot and imported into another store, **Then** all metadata is preserved losslessly.
2. **Given** a definition without facet metadata, **When** it is exported and imported, **Then** the snapshot content and import outcome are unchanged from today.
3. **Given** identical definitions and resource saves, **When** executed against the in-memory store and the SQLite JSON provider, **Then** registration outcomes, save outcomes, and diagnostic codes are identical, with no SQLite storage schema change.

### Edge Cases

- Metadata constraints MUST apply only when a facet value is present; absence/requiredness remains governed solely by the existing required flag (no behavior change for missing values).
- A facet with only display metadata (label, description, display order, UI hint) and no constraints MUST validate values exactly as a facet without metadata.
- Equal minimum and maximum (length or numeric range) MUST be valid metadata and accept exactly the boundary value.
- Boundary values (value length equal to min or max; numeric value equal to min or max) MUST pass validation.
- A negative minimum or maximum length MUST be rejected at registration with the stable range-invalid diagnostic code.
- An allowed-values list containing duplicate values MUST be rejected at registration with the stable allowed-values-duplicate diagnostic code.
- A default value is metadata only: it MUST NOT be automatically injected into saved resource versions; hosts read it to pre-populate forms.
- The UI hint is an opaque, non-validated string for host interpretation; it MUST NOT affect value validation.
- When both allowed values and other constraints (length, range, pattern) are declared, the default value and saved values MUST satisfy all of them.
- Pattern evaluation MUST be deterministic and safe: patterns are validated for well-formedness at registration, and pattern matching at save time MUST NOT allow unbounded execution to hang a save (a violating or non-terminating evaluation fails the value deterministically).
- Diagnostic ordering MUST be deterministic: ordered by aspect key, then facet identifier, then diagnostic code, using ordinal string ordering.
- Metadata comparisons for numeric constraints MUST use the facet's declared type semantics (integer comparison for int facets, decimal comparison for decimal facets).

### Constitution Alignment *(mandatory)*

- **Simplicity**: This feature adds optional properties to the existing facet definition model, registration-time metadata self-validation, and save-time value checks. It MUST NOT add provider registries, schedulers, localization, client-side/UI framework code, cross-facet or cross-aspect conditional rules, or async/remote validators. Facets without metadata behave exactly as today.
- **Explicitness**: Hosts opt in by declaring metadata on each facet. There is no automatic discovery, no implicit defaults injection, no hidden validation for facets that declare nothing. All rejection paths surface stable, documented diagnostic codes.
- **Dependencies**: None. The implementation MUST use existing C#/.NET and project test dependencies only.
- **Operational Impact**: No storage schema changes (definitions remain JSON payloads in the SQLite JSON provider), no configuration, no deployment steps. Local validation remains standard `dotnet build` and `dotnet test`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The facet definition model MUST support the following optional metadata: display label, description, display order, default value, allowed values (closed list), minimum/maximum string length, minimum/maximum numeric range, regular-expression pattern, and a UI hint string.
- **FR-002**: All metadata MUST be optional and additive; a definition declaring no metadata MUST register, validate, save, export, and import exactly as today.
- **FR-003**: The UI hint MUST be an opaque string carried verbatim for host interpretation (e.g., "text", "multiline", "richtext", "slug", "email", "url", "date", "datetime", "number", "toggle", "select", "tags"); the system MUST NOT restrict or interpret its value.
- **FR-004**: Registration MUST reject a facet whose minimum exceeds its maximum (string length or numeric range), or whose length bounds are negative, with the stable diagnostic code `facet-metadata-range-invalid`.
- **FR-005**: Registration MUST reject a constraint that is inapplicable to the facet's declared type (length or pattern on non-string facets; numeric range on non-numeric facets; allowed values on boolean facets) with the stable diagnostic code `facet-metadata-constraint-inapplicable`.
- **FR-006**: Registration MUST reject an empty allowed-values list with the stable diagnostic code `facet-metadata-allowed-values-empty`, and a list containing duplicates with the stable diagnostic code `facet-metadata-allowed-values-duplicate`.
- **FR-007**: Registration MUST reject an allowed-values entry whose type does not match the facet's declared type with the stable diagnostic code `facet-metadata-allowed-value-type-mismatch`.
- **FR-008**: Registration MUST reject a default value that violates the facet's declared type or any of its declared constraints with the stable diagnostic code `facet-metadata-default-value-invalid`.
- **FR-009**: Registration MUST reject a syntactically invalid regular-expression pattern with the stable diagnostic code `facet-metadata-pattern-invalid`.
- **FR-010**: Registration self-validation MUST report all metadata violations across all aspects and facets together, deterministically ordered by aspect key, then facet identifier, then diagnostic code (ordinal ordering), each violation identifying the offending aspect and facet.
- **FR-011**: Saving a resource version MUST fail when a present facet value is outside the facet's allowed-values list, with the stable diagnostic code `facet-value-not-allowed`.
- **FR-012**: Saving a resource version MUST fail when a present string facet value violates declared length bounds, with the stable diagnostic code `facet-value-length-out-of-range`.
- **FR-013**: Saving a resource version MUST fail when a present numeric facet value violates declared range bounds, with the stable diagnostic code `facet-value-out-of-range`.
- **FR-014**: Saving a resource version MUST fail when a present string facet value does not match a declared pattern, with the stable diagnostic code `facet-value-pattern-mismatch`.
- **FR-015**: Save-time validation MUST evaluate constraints only for values that are present; missing-value handling MUST remain governed exclusively by the existing required flag with no behavior change.
- **FR-016**: A failed save MUST report all facet value violations together, deterministically ordered by aspect key, then facet identifier, then diagnostic code (ordinal ordering), and MUST persist nothing.
- **FR-017**: Facets without validation metadata MUST validate at save time exactly as today (no behavior change), and display-only metadata MUST NOT affect value validation.
- **FR-018**: Default values MUST be exposed to hosts as metadata only and MUST NOT be automatically applied to saved resource versions.
- **FR-019**: Portable snapshot export and import MUST round-trip all facet metadata losslessly; definitions without metadata MUST produce snapshot content unchanged from today.
- **FR-020**: The in-memory store and the SQLite JSON provider MUST exhibit identical metadata persistence, registration self-validation, and save-time validation behavior, including identical diagnostic codes, with no SQLite storage schema changes.
- **FR-021**: The implementation MUST NOT introduce product APIs beyond the core SDK, storage schema changes, provider registries, public raw SQL, public `IQueryable<Resource>`, schedulers, localization of labels, client-side/UI framework code, cross-facet or cross-aspect conditional validation, async/remote validators, or new dependencies.

### Key Entities *(include if feature involves data)*

- **Facet Metadata**: Optional, additive declaration attached to a facet definition: display label, description, display order, default value, allowed values, string length bounds, numeric range bounds, regular-expression pattern, and UI hint string.
- **Facet Metadata Diagnostic**: Registration-time self-validation failure; carries a stable diagnostic code (`facet-metadata-range-invalid`, `facet-metadata-constraint-inapplicable`, `facet-metadata-allowed-values-empty`, `facet-metadata-allowed-values-duplicate`, `facet-metadata-allowed-value-type-mismatch`, `facet-metadata-default-value-invalid`, `facet-metadata-pattern-invalid`) plus the offending aspect key and facet identifier.
- **Facet Value Diagnostic**: Save-time validation failure; carries a stable diagnostic code (`facet-value-not-allowed`, `facet-value-length-out-of-range`, `facet-value-out-of-range`, `facet-value-pattern-mismatch`) plus the offending aspect key and facet identifier.
- **Facet Definition**: Existing typed sub-field declaration within an aspect; gains the optional metadata without changing its identity, versioning, type descriptor, or required-flag semantics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can generate a complete editing form (labels, descriptions, ordering, defaults, choices, input kinds, and constraint bounds) from a registered definition alone, with zero out-of-band schema files.
- **SC-002**: 100% of the defined invalid metadata combinations are rejected at registration with their documented stable diagnostic codes, verified by tests covering every code.
- **SC-003**: 100% of the defined facet value violations fail the save with their documented stable diagnostic codes, and no violating resource version is ever persisted, verified by tests covering every code.
- **SC-004**: Definitions and saves without facet metadata produce byte-identical behavior to the previous release: all pre-existing tests pass without modification to expected outcomes.
- **SC-005**: A definition with full metadata exported and re-imported through a portable snapshot is metadata-equivalent to the original in 100% of round-trip tests.
- **SC-006**: The full registration and save-validation test matrix produces identical outcomes and diagnostic codes on the in-memory store and the SQLite JSON provider, with zero SQLite schema changes.
- **SC-007**: Repeated validation of the same definition or resource version yields identical diagnostics in identical order across runs (deterministic output).

## Assumptions

- Facet declared types remain the existing descriptor set ("string", "int", "decimal", "bool", "datetime"); length and pattern constraints apply to string facets, numeric range applies to int and decimal facets, and allowed values apply to string, int, decimal, and datetime facets.
- Datetime facets support allowed values and default values but not numeric range bounds in this slice; declaring range bounds on a datetime facet is rejected as constraint-inapplicable (a future slice may add date ranges).
- Diagnostic codes follow the repository's established lowercase kebab-case convention (e.g., `tenant-scope-invalid`, `invalid-export-scope`).
- Registration-time and save-time failures surface through the existing registration and save failure channels used by prior features; no new reporting pipeline is introduced.
- Regular-expression patterns use the platform's standard pattern syntax; hosts are responsible for authoring sensible patterns, while the system guarantees well-formedness checks at registration and bounded, deterministic evaluation at save time.
- Display order is a plain integer with host-defined semantics; the system does not enforce uniqueness or contiguity of display orders across facets.
