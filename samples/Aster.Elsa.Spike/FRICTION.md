# Elsa spike: friction log

Raw findings from running `Program.cs` against SQLite on `main` (2026-10-20). The spike models three Elsa-like control-plane entities (labels on workflow definitions, agent configurations, secret metadata), plus a workflow-definition stub that carries labels. Evidence types: F2–F7, F9 and F12 were observed at runtime. F1 and F8 come from the code the spike had to write or read (no API to call; a context type without the field). F10 combines a runtime result (the import succeeded and preserved IDs) with a conditional, code-reading note about remapping. F11 is a code-reading note. Interpretation, prioritisation and the go/no-go call happen in #78.

| ID | Area | Finding | Evidence | Severity | Program task |
|---|---|---|---|---|---|
| F1 | Definitions | Facets can only be declared by hand-building `AspectDefinition`/`FacetDefinition` records. `ResourceDefinitionBuilder` has no `WithFacet(id, type)`, and `WithAspect<T>()` declares no facets. | `Model` helper in `Program.cs` | High (DX) | #124 |
| F2 | Definitions | Unknown facet types (`string[]`, `object`, typos) register without a diagnostic and silently skip validation. | All four definitions register | High | #123 |
| F3 | Querying | "Workflows labelled `lbl-billing`" via `Contains` also matches `lbl-billing-eu`. Substring matching on a serialized array produces false positives. | Returned `[wf-invoice, wf-vat]` | **Blocker** for tagging | #82, #83 |
| F4 | References | A workflow referencing a non-existent label saves. There is no reference type and no integrity check. | `wf-dangling` saved | High | #86, #87 |
| F5 | Validation | A facet declared `IsRequired` can be missing. | `lbl-unnamed` saved without `Name` | High | #123 |
| F6 | Validation | A `datetime` facet accepts `"not-a-date"`. Declared types are not enforced. | `sec-wrong-type` saved | High | #123 |
| F7 | Activation | Promotion between channels needs two calls (read the active version in one channel, activate it in the next). Rollback through historical activation works. | Promotion section of S2 | Low | #78 to decide (Elsa module convenience) |
| F8 | Hooks | Activation hook contexts lack `DefinitionId`, so "when an Agent is activated in production" needs an extra read per event. | `ResourceActivationLifecycleContext` | Medium | New task (prerequisite for #116) |
| F9 | Lifecycle | Archiving a secret that an agent references succeeds silently. There is no "used by" lookup. | `succeeded=True`, 0 diagnostics | Medium | #88, #90 |
| F10 | Portability | Export/import into another tenant works and preserves IDs. If IDs were remapped, references inside payloads would not be rewritten, because they are untyped strings. | Import status `Imported` | Medium | #89 |
| F11 | DX | With dictionary aspects, every aspect and facet name is a string literal. Typed query helpers only help POCO aspects, which have no derived facets. | — | Medium | #124 |
| F12 | Lifecycle | Archived resources stay in default query results unless `LifecycleState` is set. Elsa-style listings would show archived secrets. | `sec-openai` visible after archiving | Medium (design question) | #78 to decide |

## What worked without friction

- Tenant isolation: tenant B saw none of tenant A's secrets until the import.
- Channel activation, single-active semantics per channel, and rollback through historical activation.
- `ResourceQuery` with `Scope = Active` and an `ActivationChannel` ("agents active in production").
- Date range queries on `datetime` facets in SQLite (secrets expiring within 30 days).
- 038 facet metadata: `Pattern` rejected an invalid label colour.
- Lifecycle hooks fired once per activation, with tenant, resource, version and channel.
- Portability with `DefinitionWithResources` into a different tenant.
