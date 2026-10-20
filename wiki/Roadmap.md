# Roadmap

Aster's roadmap is tracked in one place: the **[Aster 1.0 program](https://github.com/valence-works/aster/issues/58)**, a hierarchy of GitHub issues (program → epics → tasks). Each task maps to roughly one Spec Kit slice and one PR.

| Phase | Focus |
|---|---|
| **A** | Reset & foundation: delivery process, CI/packaging/API stability, Elsa consumer spike |
| **B** | Model completeness: facet types (guid, arrays, object), resource references, shared aspect definitions |
| **C** | Production readiness: PostgreSQL JSONB provider, performance & scale |
| **D** | Integration & experience: JSON Schema export and `Aster.AspNetCore`, Aster Studio, Elsa integration, reference samples |

Related documents:

- [`docs/ExecutionRoadmap.md`](../docs/ExecutionRoadmap.md): the slices that have already landed (001–038 and Aster Studio).
- [`docs/Roadmap.md`](../docs/Roadmap.md): the architecture narrative and design decisions behind the phases.
