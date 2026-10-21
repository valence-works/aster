# UI hints

`FacetMetadata.UiHint` tells a host UI which editor to render for a facet. Aster never validates or interprets it (spec 038): a hint changes presentation, never what a value may be. This page defines the standard vocabulary, so the same aspect renders the same way in Aster Studio, Elsa Studio or any other host. The names are available as constants on `Aster.Core.Models.Definitions.UiHints`.

```csharp
.WithFacet("Body", "string", metadata: new FacetMetadata { UiHint = UiHints.Markdown })
```

## Standard hints

| Hint | Constant | Editor | Facet types |
|---|---|---|---|
| `text` | `UiHints.Text` | Single-line text input | `string` |
| `multiline` | `UiHints.Multiline` | Plain multi-line text area | `string` |
| `markdown` | `UiHints.Markdown` | Markdown editor; the value is Markdown source | `string` |
| `html` | `UiHints.Html` | Rich-text (WYSIWYG) editor; the value is HTML | `string` |
| `slug` | `UiHints.Slug` | Slug input that can derive the value from a title | `string` |
| `email` | `UiHints.Email` | Email input | `string` |
| `url` | `UiHints.Url` | URL input | `string` |
| `number` | `UiHints.Number` | Numeric input honoring `Minimum` / `Maximum` | `int`, `decimal` |
| `select` | `UiHints.Select` | Dropdown over `AllowedValues`; multi-select for arrays | scalars and their arrays |
| `radio` | `UiHints.Radio` | Radio group over `AllowedValues` | `string`, `int`, `decimal` |
| `checkbox` | `UiHints.Checkbox` | Checkbox; checkbox list over `AllowedValues` for arrays | `bool`, scalar arrays |
| `switch` | `UiHints.Switch` | On/off switch | `bool` |
| `date` | `UiHints.Date` | Date picker; the value is the date at midnight UTC | `datetime` |
| `datetime` | `UiHints.DateTime` | Date and time picker | `datetime` |
| `tags` | `UiHints.Tags` | Free-entry tag input | `string[]` |
| `reference-picker` | `UiHints.ReferencePicker` | Search resources of the facet's `TargetDefinitionIds` | `reference`, `reference[]` |
| `json` | `UiHints.Json` | JSON editor | `object`, `string` holding JSON |
| `code` | `UiHints.Code` | Code editor (monospace) | `string` |
| `secret` | `UiHints.Secret` | Masked input | `string` |

`secret` is presentation only. Aster stores and returns the value as is, so it is not a place for credentials that need protection at rest.

## Rules for hosts

- **Fall back by type.** When a facet has no hint, or one the host doesn't know, pick an editor from the facet type: text for `string`, a number input for `int` / `decimal`, a switch or checkbox for `bool`, a date-time picker for `datetime`, a JSON editor for `object`, and a tag or list input for arrays.
- **`AllowedValues` wins over free entry.** A facet with `AllowedValues` and no hint renders as `select`.
- **A hint never relaxes validation.** Save-time validation enforces types and constraints whatever the editor, so a UI only needs to be helpful, not authoritative.
- **Custom hints use a prefix.** Hints outside this list should carry a host or vendor prefix (`acme:color-picker`), so they never collide with future standard names.

## Mapping to Elsa Studio

Elsa Studio picks its input editors by UI hint, through the `UIHints` names in Elsa 3. The Aster module (#114) maps the standard hints like this. Treat the right-hand column as the intended mapping; #114 checks it against the Elsa version it targets.

| Aster hint | Elsa Studio editor (`UIHints`) |
|---|---|
| `text`, `slug`, `email`, `url` | `SingleLine` |
| `multiline`, `markdown`, `html` | `MultiLine` |
| `code` | `CodeEditor` |
| `json` | `JsonEditor` |
| `select` | `DropDown` (scalar) / `CheckList` (array) |
| `radio` | `RadioList` |
| `checkbox` | `Checkbox` (bool) / `CheckList` (array) |
| `switch` | `SwitchEditor` |
| `date`, `datetime` | `DateTimePicker` |
| `tags` | `MultiText` |
| `number`, `secret`, `reference-picker` | No direct equivalent: `SingleLine`, or an Aster-provided editor |

## Aster Studio

Studio's resource editor (`ClientApp/src/components/DynamicResourceForm.tsx`) uses the constants mirrored in `ClientApp/src/lib/uiHints.ts`. It renders each hint as follows:
- **Dedicated editors:** `text`, `multiline`, `slug`, `email`, `url`, `number`, `select`, `checkbox`, `switch`, `date`, `datetime`, `tags` and `secret`.
- **Simplified for now:**
  - `radio` renders as a dropdown.
  - `markdown`, `html`, `code` and `json` render as a large monospace text area.
  - `reference-picker` renders as a text input.

It also accepts the earlier Studio-only names `toggle` (= `switch`) and `richtext` (= `markdown`), so databases seeded before #107 still render.
