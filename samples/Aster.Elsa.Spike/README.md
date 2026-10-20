# Aster × Elsa consumer spike

Throwaway spike for [#77](https://github.com/valence-works/aster/issues/77). It answers one question with running code: what happens when an Elsa-like host stores its control-plane entities in Aster?

It models these entities on the SQLite JSON provider:

- **Labels** attached to workflow definitions (a horizontal aspect).
- **Agents**: prompt, model settings and secret references, versioned and promoted through `staging` and `production` channels, with rollback.
- **Secrets metadata**: provider, scope and expiry. No secret values.

The scenarios cover tenants, export/import between tenants, archiving, and lifecycle hooks as a stand-in for Elsa notifications.

```bash
dotnet run --project samples/Aster.Elsa.Spike
```

The program asserts the behaviour it relies on and prints findings F1–F12. [FRICTION.md](FRICTION.md) is the friction log, with evidence and the program task each finding maps to.

This is not production code and not an Elsa integration. That work is epic #69, gated on the findings in #78.

> **Note:** Since #123 (facet type enforcement), registration rejects the spike's `string[]` and `object` facet types until arrays (#82) and objects (#84) land, so the program stops at registration. The findings in FRICTION.md were recorded before that change, which closed F2, F5 and F6.
