# JSON Schema Export

`IResourceSchemaExporter` turns a `ResourceDefinition` into a [JSON Schema 2020-12](https://json-schema.org/draft/2020-12/schema) document. Hosts with their own UIs can then generate forms with standard tools such as react-jsonschema-form, JSONForms or Blazor form generators. Aster doesn't render anything itself: the schema is the contract between the definition and the UI.

```csharp
var exporter = services.GetRequiredService<IResourceSchemaExporter>(); // registered by AddAsterCore
var definition = await definitionStore.GetDefinitionAsync("Article");
JsonObject schema = exporter.Export(definition!);              // the whole resource
JsonObject seo = exporter.ExportAspect(definition!.AspectDefinitions["Seo"]); // one aspect payload
```

## Shape

- **Root:** an object with one property per aspect attachment key.
- **Aspects:** each aspect is an object (or `null`) with one property per facet, and `required` lists its required facets. Named attachments appear under their full key (`Price:Listing`).
- **Ordering:** output is deterministic. Aspects are ordered by key (ordinal). Facets are ordered by `DisplayOrder`, then by declaration order, which is the order form generators render them in.
- **Property names:** these are facet IDs. Save-time validation also accepts the camelCase spelling of a facet ID, but the schema only describes the declared name, so emit payloads with the declared names.

## Mapping

| Facet | JSON Schema |
|---|---|
| `string` | `type: string` |
| `int` | `type: integer` |
| `decimal` | `type: number` |
| `bool` | `type: boolean` |
| `datetime` | `type: string`, `format: date-time` |
| `guid` | `type: string`, `format: uuid` |
| `reference` | `type: string`, `pattern: "\S"` (a non-blank resource ID) |
| `object` | `type: object` |
| `T[]` | `type: array`, `items` = the schema of `T`; elements are never null |
| optional facet | the type also allows `null` (absent and null are the same to save-time validation) |
| `IsRequired` | listed in the aspect's `required`; a required array also gets `minItems: 1` |
| `Label` | `title` |
| `Description` | `description` |
| `DefaultValue` | `default` |
| `AllowedValues` | `enum` (plus `null` when the facet is optional); on `items` for arrays |
| `MinLength` / `MaxLength` | `minLength` / `maxLength` |
| `Minimum` / `Maximum` | `minimum` / `maximum` (inclusive) |
| `Pattern` | `pattern` |
| `MinItems` / `MaxItems` / `UniqueItems` | `minItems` / `maxItems` / `uniqueItems` |

Value constraints of an array facet (`AllowedValues`, lengths, `Pattern`, range) apply to each element, so they are emitted on `items`.

## Extensions

Hosts read these `x-aster-*` keywords; JSON Schema validators ignore them.

| Keyword | Where | Value |
|---|---|---|
| `x-aster-definition` | root | `{ definitionId, version }` |
| `x-aster-aspect` | aspect | `{ aspectDefinitionId, version, shared, requiresName }`; `shared` marks a pinned shared aspect (ADR 0003) |
| `x-aster-type` | facet | the declared facet type, for example `reference[]` |
| `x-aster-uiHint` | facet | `UiHint`, verbatim |
| `x-aster-displayOrder` | facet | `DisplayOrder` |
| `x-aster-reference` | reference facet | `{ targetDefinitionIds?, resolution? }`, from `TargetDefinitionIds` and `ReferenceResolution` |
| `x-aster-allowedValues` | guid and datetime facet | `AllowedValues` (see below) |

## Guarantee and known gaps

The schema is never stricter than save-time validation: every resource Aster accepts also validates against its exported schema. `ResourceSchemaExportTests` checks this over a table of payloads covering every type and constraint, and checks the output against the 2020-12 meta-schema.

In a few places the schema is looser than Aster, or the two can disagree:

- **`format` is an annotation.** JSON Schema 2020-12 doesn't assert `format` by default, and Aster's datetime parsing accepts more than RFC 3339 (for example `2026-07-01`). A malformed date or GUID therefore passes the schema and is rejected on save.
- **Guid and datetime allowed values.** Aster compares GUIDs case-insensitively and datetimes as instants, but `enum` compares JSON text exactly. These lists are exported as the `x-aster-allowedValues` annotation instead of `enum`, so a UI can still offer them as choices.
- **Pattern dialect.** Aster evaluates `Pattern` with .NET regular expressions, while JSON Schema specifies ECMA-262. Patterns that use only the common subset (character classes, anchors, quantifiers, groups, alternation) behave the same. Avoid .NET-only constructs: inline options such as `(?i)`, the anchors `\A` and `\Z`, and lookbehind with variable length. Unicode categories (`\p{L}`) need ECMA-262's `u` flag, which validators don't apply consistently. Patterns are unanchored in both, so use `^…$` to match the whole value.
- **String length units.** Aster counts UTF-16 code units, and JSON Schema counts code points. They differ only for characters outside the Basic Multilingual Plane, such as emoji: a value with such a character can satisfy `MinLength` in Aster but not `minLength` in the schema.
- **Escaping.** `JsonObject.ToJsonString()` uses the default encoder, which escapes characters such as `+` as `\u002B`. The schema still means the same. For readable output in a JSON response, serialize with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, but don't embed that output in HTML.
