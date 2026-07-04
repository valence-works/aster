# Data Model: Facet Validation and UI Metadata

## FacetMetadata (new record, `Aster.Core.Models.Definitions`)

Optional, additive declaration attached to a `FacetDefinition`. Every property is optional; an absent property means "not declared". Serialized inside the existing definition JSON payloads with null suppression, so undeclared properties are omitted.

Fields:

- `Label` (`string?`): Display label for form generation. Display-only; never affects validation.
- `Description` (`string?`): Human-readable description. Display-only.
- `DisplayOrder` (`int?`): Plain integer with host-defined semantics; uniqueness/contiguity not enforced.
- `DefaultValue` (`object?`): Metadata-only default for form pre-population; never injected into saved versions. Must satisfy the facet's declared type and all declared constraints.
- `AllowedValues` (`IReadOnlyList<object>?`): Closed list of permitted values. When declared: non-empty, no duplicates (after normalization), every entry matches the declared type.
- `MinLength` (`int?`) / `MaxLength` (`int?`): String length bounds (UTF-16 code units). String facets only; non-negative; `MinLength <= MaxLength` when both declared; equal bounds accept exactly that length.
- `Minimum` (`decimal?`) / `Maximum` (`decimal?`): Numeric range bounds. Int and decimal facets only; whole numbers required for int facets; `Minimum <= Maximum` when both declared; boundary values pass.
- `Pattern` (`string?`): .NET regular-expression pattern. String facets only; must be well-formed at registration; evaluated with a bounded match timeout at save time.
- `UiHint` (`string?`): Opaque, non-validated string carried verbatim for host interpretation (e.g., "text", "multiline", "select"). Never affects validation.

Relationships:

- Owned by `FacetDefinition.Metadata` (`FacetMetadata?`, default `null`). `null` means the facet behaves exactly as before this feature.

## FacetDefinition (existing record, extended)

- Gains `Metadata` (`FacetMetadata?`), annotated to be omitted from JSON when null. Identity (`FacetDefinitionId`, `Id`, `Version`), `Type`, and `IsRequired` semantics are unchanged.

## Constraint Applicability Matrix (by declared `FacetDefinition.Type`)

| Constraint       | string | int | decimal | bool | datetime |
|------------------|--------|-----|---------|------|----------|
| AllowedValues    | ✔      | ✔   | ✔       | ✘    | ✔        |
| Min/MaxLength    | ✔      | ✘   | ✘       | ✘    | ✘        |
| Minimum/Maximum  | ✘      | ✔   | ✔       | ✘    | ✘        |
| Pattern          | ✔      | ✘   | ✘       | ✘    | ✘        |
| DefaultValue     | ✔      | ✔   | ✔       | ✔    | ✔        |
| Display metadata | ✔      | ✔   | ✔       | ✔    | ✔        |

Declaring an inapplicable constraint (✘) is rejected at registration with `facet-metadata-constraint-inapplicable`. An unknown declared type applies no metadata applicability assumptions beyond display metadata and default-value type-check skipping (unchanged behavior otherwise).

## FacetMetadataDiagnostic (new record, `Aster.Core.Models.Definitions`)

Registration-time self-validation failure.

Fields:

- `Code` (`string`): One of the stable codes below.
- `Message` (`string`): Human-readable, actionable explanation.
- `AspectKey` (`string`): Attachment key of the offending aspect (e.g., `PriceAspect` or `PriceAspect:Sale`).
- `FacetDefinitionId` (`string`): Offending facet identifier.

Stable codes (constants on `FacetMetadataDiagnosticCodes`):

| Code | Condition |
|------|-----------|
| `facet-metadata-range-invalid` | `MinLength > MaxLength`, negative `MinLength`/`MaxLength`, `Minimum > Maximum`, or non-whole numeric bound on an int facet |
| `facet-metadata-constraint-inapplicable` | Constraint declared for a type outside the applicability matrix |
| `facet-metadata-allowed-values-empty` | `AllowedValues` declared but empty |
| `facet-metadata-allowed-values-duplicate` | `AllowedValues` contains duplicates after normalization |
| `facet-metadata-allowed-value-type-mismatch` | An `AllowedValues` entry does not normalize to the declared type |
| `facet-metadata-default-value-invalid` | `DefaultValue` violates the declared type or any declared constraint |
| `facet-metadata-pattern-invalid` | `Pattern` is not a well-formed .NET regular expression |

## FacetValueDiagnostic (new record, `Aster.Core.Models.Definitions`)

Save-time value validation failure.

Fields:

- `Code` (`string`): One of the stable codes below.
- `Message` (`string`): Human-readable, actionable explanation.
- `AspectKey` (`string`): Attachment key of the offending aspect on the resource.
- `FacetDefinitionId` (`string`): Offending facet identifier.

Stable codes (constants on `FacetValueDiagnosticCodes`):

| Code | Condition |
|------|-----------|
| `facet-value-not-allowed` | Present value outside the declared `AllowedValues` list |
| `facet-value-length-out-of-range` | Present string value shorter than `MinLength` or longer than `MaxLength` |
| `facet-value-out-of-range` | Present numeric value below `Minimum` or above `Maximum` |
| `facet-value-pattern-mismatch` | Present string value does not match `Pattern` (including bounded-evaluation timeout) |

## Exceptions (new, `Aster.Core.Exceptions`)

- `FacetMetadataValidationException`: thrown by definition registration; carries `IReadOnlyList<FacetMetadataDiagnostic> Diagnostics` (deterministically ordered).
- `FacetValueValidationException`: thrown by resource save (create/update); carries `IReadOnlyList<FacetValueDiagnostic> Diagnostics` (deterministically ordered). A throw means nothing was persisted and no before-save lifecycle hook ran for that save.

## Validation Rules

Registration (`FacetMetadataValidator`, invoked by both definition stores' `RegisterDefinitionAsync`):

1. For every `(aspectKey, aspectDefinition)` in `ResourceDefinition.AspectDefinitions` and every facet with non-null `Metadata`, evaluate all applicable rules; collect every violation (no short-circuit across facets).
2. Constraint applicability per the matrix; bound coherence (`min <= max`, non-negative lengths, whole int bounds); allowed-values non-empty / distinct / type-matched; pattern well-formedness; default-value conformance against type + all declared constraints.
3. Sort diagnostics by `AspectKey`, then `FacetDefinitionId`, then `Code` (all `StringComparer.Ordinal`); throw `FacetMetadataValidationException` if any exist; otherwise registration proceeds unchanged.

Save (`FacetValueValidator`, invoked by `DefaultResourceManager` and `InMemoryResourceManager` create/update before hooks and writes):

1. Resolve the definition exactly as the save path does today; when the definition or metadata is absent, no metadata checks run (behavior unchanged).
2. For every aspect attachment with a matching aspect payload on the resource, resolve each metadata-bearing facet's value using dictionary / `JsonElement` / POCO-property semantics (mirroring `IndexProjectionEvaluator`).
3. Absent or null values are skipped entirely — requiredness stays governed solely by the existing `IsRequired` flag. Values that cannot be normalized to the declared type are skipped (no new rejection channel in this slice).
4. Present, normalized values are checked against `AllowedValues`, length bounds, numeric range, and pattern (bounded evaluation); a value must satisfy all declared constraints.
5. Sort diagnostics (same ordinal triple ordering); throw `FacetValueValidationException` if any exist — before lifecycle hooks and before any write, so nothing persists.

State transitions: none — metadata is immutable definition data; new definition versions carry new metadata snapshots exactly like every other definition property.

## Normalization Semantics (shared by both validators)

| Declared type | Accepted inputs | Comparison |
|---------------|-----------------|------------|
| `string` | `string`, `JsonElement` string | Ordinal equality; `string.Length` for bounds |
| `int` | boxed integral types, integral `JsonElement` numbers | Integer (`long`) comparison |
| `decimal` | boxed numeric types, `JsonElement` numbers | `decimal` comparison |
| `bool` | `bool`, `JsonElement` true/false | Boolean equality |
| `datetime` | `DateTime`, `DateTimeOffset`, ISO 8601 strings, `JsonElement` strings | Normalized `DateTime` equality |
