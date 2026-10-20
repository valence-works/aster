# Elsa consumer spike: findings and decision

- **Date:** 2026-10-20
- **Spike:** [`samples/Aster.Elsa.Spike`](../../samples/Aster.Elsa.Spike) (#77)
- **Raw log:** [`FRICTION.md`](../../samples/Aster.Elsa.Spike/FRICTION.md)
- **Program:** [#58](https://github.com/valence-works/aster/issues/58)

## Question

Can an Elsa-like host move its control-plane entities onto Aster? The spike modelled labels on workflow definitions, agent configurations promoted through channels, and secret metadata. If yes, what has to change first?

## Answer

**Go, conditional on Phase B.** The parts of Aster that are hard to retrofit worked without friction in the spike:
- tenant isolation
- immutable versions
- channel activation with rollback
- lifecycle hooks
- cross-tenant portability

Every blocking gap sits in the **schema layer**: facet types, required and type enforcement, references, and typed aspects. All of it is already planned in Phase B. No new epic is needed, and the program order stands with one addition (#127).

## What worked

| Capability | Evidence in the spike |
|---|---|
| Tenant isolation | Tenant B saw no tenant A data until the explicit import. |
| Channel promotion and rollback | v1 → production, v2 → staging, promote v2, roll back to v1 through historical activation. |
| "Active in channel X" queries | `Scope = Active`, `ActivationChannel = "production"` returned exactly the promoted agent. |
| Date range queries | "Secrets expiring within 30 days" on a `datetime` facet in SQLite. |
| Declarative facet validation (038) | `Pattern` rejected an invalid label colour. |
| Lifecycle hooks | Exactly one notification per activation, with tenant, resource, version and channel. |
| Portability | `DefinitionWithResources` export from tenant A, imported into tenant B. |

## Gaps and where they are handled

| Finding | Gap | Severity | Handled by |
|---|---|---|---|
| F3 | No array operators: `Contains` on a label array returns false positives | Blocker for tagging | #82, #83 |
| F2, F5, F6 | Unknown types accepted; `IsRequired` and declared types not enforced | High | #123 |
| F1, F11 | No `WithFacet`; `WithAspect<T>()` declares no facets; string-literal names everywhere | High (DX) | #124 |
| F4 | References are unchecked strings | High | #86, #87 |
| F9 | Archiving a referenced resource is silent | Medium | #88, #90 |
| F10 | Import remapping would not rewrite references | Medium | #89 |
| F8 | Activation hook contexts lack `DefinitionId` | Medium (blocks #116) | **#127 (new)** |
| F7 | Promotion between channels takes two calls | Low | Decision below |
| F12 | Archived resources appear in default queries | Medium | Decision below |

## Decisions

- **F7, no `PromoteAsync` in core.** Reading the active version in one channel and activating it in another is two explicit calls. That is in line with the constitution's explicitness principle, and a core API would not earn its keep. The Elsa module (#115) and `Aster.AspNetCore` (#108) can offer a promote action on top.
- **F12, core stays explicit; hosting defaults to hiding archived resources.** `ResourceQuery` keeps returning every lifecycle state unless `LifecycleState` is set, because changing that default silently would break existing hosts. `Aster.AspNetCore` list endpoints (#108) and the Elsa module will exclude archived and soft-deleted resources by default, with an opt-in to include them.
- **Elsa module (#69): go.** Its work starts after #123, #124, #82, #83, #86–#89 and #102 (SQL Server strategy). The first entity to move (#118) should be **agent definitions**: they exercise channels and rollback, which worked best in the spike, and need references, which Phase B delivers.
- **Control-plane boundary:** confirmed. Nothing in the spike tempted runtime data into Aster. The boundary ADR (#79) records this.

## Program changes

- New task **#127** (`DefinitionId` in activation hook contexts) under epic #69, as a prerequisite for #116.
- #108 picks up the default exclusion of archived and soft-deleted resources in list endpoints.
- Phase B order: #123 → #124 → #81 / #82 → #83 → #84 → #85, then references (#86–#90). Shared aspects (#91–#96) can run alongside references.
