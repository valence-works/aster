# ADR 0002: Facet type model

- **Status:** Accepted
- **Date:** 2026-10-20
- **Program task:** [#80](https://github.com/valence-works/aster/issues/80) (epic [#62](https://github.com/valence-works/aster/issues/62))
- **Unblocks:** #81 (guid), #82 (arrays), #83 (array operators), #84 (object), #86 (references), #106 (JSON Schema export)

## Context

`FacetDefinition.Type` is a free-form string. `FacetMetadataValidator.ClassifyFacetType` recognises `string`, `int` (aliases `int32`, `int64`, `long`), `decimal`, `bool` (alias `boolean`) and `datetime`. Anything else is classified as `Unknown` and silently skipped.

Reading the current code surfaced more gaps than the missing types:

| Observation | Where | Consequence |
|---|---|---|
| Unknown type descriptors are accepted at registration and skipped at save | `FacetMetadataValidator`, `FacetValueValidator` | A typo such as `"strnig"` disables validation silently. |
| Values are never checked against the declared type | `FacetValueValidator.ValidateFacetValue` skips values that do not normalize | `{ "Amount": "abc" }` saves on a `decimal` facet. |
| Validation runs only when `FacetMetadata` is declared | `FacetValueValidator.Validate` | Type checks would not run for plain facets even if added. |
| `IsRequired` is never enforced | only read by Aster Studio | A "required" facet can be missing or null. |
| `WithAspect<T>()` declares **no** facets for the POCO's properties | `ResourceDefinitionBuilder` | Code-first typed aspects carry no schema, so there is nothing to validate, render, or export. |
| `WithTypedFacet<T>()` uses the CLR type name as the facet ID and type | `ResourceDefinitionBuilder` | `WithTypedFacet<string>()` creates a facet called `String`. The API is misleading. |
| The index model is ahead of the facet model | `IndexFieldType` has `Guid` and `KeywordArray` | Index projections can declare types that facets cannot. |

The program needs `guid`, arrays, `object`, and references (epics #62 and #63), and JSON Schema export (#106) needs a reliable type for every facet.

## Decision

### 1. Keep a string descriptor; define a closed grammar

`FacetDefinition.Type` stays a `string`. Persisted definitions, portable snapshots, and the SQLite schema are unchanged. The accepted grammar for 1.0 is:

```
type    := scalar | element "[]"
scalar  := element | "object"
element := "string" | "int" | "decimal" | "bool" | "datetime" | "guid" | "reference"
```

- Matching is case-insensitive. The canonical form is lowercase.
- Existing aliases stay accepted on input (`int32`, `int64`, `long`, `boolean`).
- One array level only. `object[]` (excluded by `element`) and nested arrays are **not** allowed in 1.0. They have no portable query semantics and no consumer yet.
- Reference targets and resolution live in `FacetMetadata`, not in the type string (see §6).

**Rejected:** a structured `FacetType` record (`{ Kind, ElementType, ReferenceTargets }`). It is more expressive, but it changes the JSON shape of every persisted definition and snapshot and needs a migration story. Nothing in 1.0 needs generic nesting.

### 2. The type set is closed for 1.0

Registration rejects a descriptor outside the grammar with a new stable diagnostic, `facet-type-unknown`. Editor semantics such as markdown, colour, or slug belong in `FacetMetadata.UiHint`, not in the type: the type describes the data shape and the UI hint describes the editor. Host-extensible types are deferred until a consumer needs one.

**Compatibility:** pre-1.0 breaking change for hosts that used ad-hoc type strings. Such definitions now fail registration with a clear diagnostic instead of silently skipping validation.

### 3. Values are checked against the declared type on every save

Every facet with a declared type is validated on create and update, whether or not it has metadata. A present, non-null value that does not normalize to the type is rejected with a new stable diagnostic, `facet-value-type-mismatch`. Metadata constraints still apply only when they are declared.

Unchanged paths:
- Portability import and explicit schema upgrades keep writing data as-is. They preserve history and must not reject versions saved under older rules.
- Reading never validates.

### 4. `IsRequired` is enforced on save

When an aspect instance is present, a required facet that is missing or null is rejected with a new stable diagnostic, `facet-value-required`. For array facets, required also means **at least one element**. This matches the portable `Exists` semantics, where an empty array does not exist, and matches what a form means by "required tags".

Aspect-level presence is not governed in 1.0. A missing aspect instance is not a facet violation.

**Compatibility:** an existing resource whose latest version violates a required or type rule can still be read and exported, but the next update must fix the value. This is acceptable pre-1.0 and must be called out in the release notes.

### 5. Normalization and storage per type

| Type | Accepted input | Stored JSON | Notes |
|---|---|---|---|
| `string` | `string` | string | Ordinal comparison; UTF-16 length. |
| `int` | integral numbers within `long` | number | Normalized to `long`. |
| `decimal` | any number representable as a CLR `decimal` (±7.9×10^28, 28–29 significant digits) | number | Normalized to `decimal`. Values outside that range, `NaN` and infinities are type mismatches. |
| `bool` | `bool` | `true`/`false` | |
| `datetime` | `DateTime`, `DateTimeOffset`, invariant-culture date string | ISO-8601 UTC string (`yyyy-MM-ddTHH:mm:ss.fffffffZ`) | Same format as `QueryDateTimeValue`. `DateTimeOffset` and strings with an offset convert by that offset. `DateTime` with `Kind = Utc` is used as is, `Kind = Local` converts through `ToUniversalTime()`, and `Kind = Unspecified` (and strings without an offset) is read as UTC. These are the rules `FacetMetadataValidator` already applies; no other time-zone inference happens. |
| `guid` | `Guid`, or a string parseable as a GUID | lowercase `D` string | Matches the roadmap's portable GUID rule. |
| `reference` | non-empty `string` (a `ResourceId`) | string | See §6. |
| `object` | JSON object, dictionary, or POCO | JSON object | Stored verbatim; no nested facet definitions in 1.0. |
| `T[]` | any enumerable of `T` (not `string`) | JSON array of normalized `T` | `null` elements are rejected with `facet-value-type-mismatch`, and the diagnostic carries the element index. |

### 6. References are plain `ResourceId` strings; resolution is per facet

A reference value is the target's `ResourceId` string. How it resolves is declared once per facet in metadata:
- `TargetDefinitionIds`: the allowed target types.
- `ReferenceResolution`: `Latest` (default) or a named activation channel.

**Rejected for 1.0: per-value version pins** (`{ resourceId, version }`). They split the stored shape (string or object), complicate every query and index path (`$.facet` vs `$.facet.resourceId`, `json_each` over objects), and no consumer has asked for them. A host that needs an exact pin can store the version in a sibling `int` facet.

**Impact on the plan:**
- #86 drops "optional version/channel pin per value".
- #90 drops "never prune versions pinned by references". It keeps reporting inbound references on archive and soft-delete.

### 7. Metadata applicability

| Constraint | Applies to |
|---|---|
| `AllowedValues`, `DefaultValue` | all scalars except `bool` and `object`, and their arrays (per element) |
| `MinLength`, `MaxLength`, `Pattern` | `string`, `string[]` (per element) |
| `Minimum`, `Maximum` | `int`, `decimal`, and their arrays (per element) |
| `MinItems`, `MaxItems`, `UniqueItems` (new, #82) | arrays only |
| `TargetDefinitionIds`, `ReferenceResolution` (new, #86) | `reference`, `reference[]` |
| `Label`, `Description`, `DisplayOrder`, `UiHint` | all types |

Out-of-matrix declarations keep failing with `facet-metadata-constraint-inapplicable`.

### 8. Code-first typed aspects derive their facets

`WithAspect<T>()` and `WithNamedAspect<T>(name)` declare one facet per public instance property that has a getter:

| CLR type | Facet type |
|---|---|
| `string` | `string` |
| `int`, `long`, `short`, `byte` (and unsigned variants that fit in `long`) | `int` |
| `decimal`, `double`, `float` | `decimal` |
| `bool` | `bool` |
| `DateTime`, `DateTimeOffset` | `datetime` |
| `Guid` | `guid` |
| `enum` | not derived in 1.0 (see the Enums bullet) |
| `Nullable<T>` of any type above | the type for `T` (nullability does not change `IsRequired`) |
| `T[]`, `IEnumerable<T>`, `List<T>`, `IReadOnlyList<T>`, `ISet<T>` of a scalar above | `T[]` |
| any other class, record, or struct | `object` |

- **Facet ID:** the property name. Reads already tolerate camelCase storage.
- **`IsRequired`:** set only from the C# `required` modifier (`RequiredMemberAttribute`). Nullability annotations are not used: too implicit, and value types always have a default.
- **References:** never inferred. Hosts declare them explicitly.
- **Enums (amended during #124):** enum properties produce no facet for now. The stores serialize typed aspects with the same options as lifecycle markers and policy kinds, which SQLite compares as integers, so writing enums as names would change the stored format. A `string` facet over integer-stored enums would make later updates fail with a type mismatch. The representation is decided in [#131](https://github.com/valence-works/aster/issues/131).
- **Explicit API:** a new `WithFacet(string facetId, string type, bool isRequired = false)` builder method declares facets on dictionary-style aspects, and `WithFacetMetadata(string facetId, FacetMetadata metadata)` targets a facet by ID. `WithTypedFacet<T>()` becomes `[Obsolete]`.

### 9. Queries stay definition-agnostic

The query AST, validator, and providers keep operating on the stored JSON shape. They do not look up facet definitions. Array operators (#83) apply to JSON arrays. This keeps querying independent of definition lookups and caching. Type enforcement on write (§3) is what makes query results trustworthy, **for values written through validated saves**. Imported versions, versions carried forward by schema upgrades, and versions written before this ADR can still hold mismatched values. Queries treat such values exactly as today: a value whose JSON shape does not fit an operator simply does not match it.

When index projections (#100) need a type, the mapping is: `string` → `Keyword`, `int` → `Integer`, `decimal` → `Decimal`, `bool` → `Boolean`, `datetime` → `DateTime`, `guid` → `Guid`, `reference` → `Keyword`, `string[]` / `reference[]` → `KeywordArray`. Other array element types get `IndexFieldType` members when a provider implements them. `object` is not indexable.

## Consequences

- One grammar and one normalization table serve validation, binders, JSON Schema export (#106) and index projections (#100).
- Persisted formats are unchanged: the definition JSON shape, the snapshot envelope, the SQLite schema, and the stored representation of facet values, including enums, which stay integers (§8, #131).
- The breaking changes are behavioural and pre-1.0: unknown types rejected, type mismatches rejected, `IsRequired` enforced.
- Two prerequisites are added to epic #62 and must land before #81–#84:
  1. [#123](https://github.com/valence-works/aster/issues/123): enforce declared types and `IsRequired` on save (§2–§4);
  2. [#124](https://github.com/valence-works/aster/issues/124): derive facets from typed aspect properties and add `WithFacet` (§8).
- #86 and #90 are simplified (§6).
