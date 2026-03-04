# Aster Wiki

Welcome to the Aster documentation wiki.

**Aster** is a .NET SDK for defining, versioning, and querying composable resources using a **Resource → Aspect → Facet** model. It provides a headless, backend-agnostic foundation for attaching reusable, cross-cutting capabilities to any resource type.

> **Current Status:** Phase 1 (Core SDK & In-Memory Engine) — active development.

---

## Navigation

| Page | Description |
|---|---|
| [Concepts & Terminology](Concepts-and-Terminology) | The core domain model: Resources, Aspects, Facets, Activation Channels |
| [Getting Started](Getting-Started) | Install, register, define, create, update, and activate resources |
| [Versioning & Activation](Versioning-and-Activation) | Immutable versions, optimistic concurrency, channel-based activation |
| [Typed Aspects & Facets](Typed-Aspects-and-Facets) | Working with C# POCOs instead of raw dictionaries |
| [Querying](Querying) | The portable `ResourceQuery` AST and in-memory evaluator |
| [DI Registration & Configuration](DI-Registration) | Service registrations and extension points |
| [Exception Reference](Exception-Reference) | All typed exceptions, when they are thrown, and how to handle them |
| [Architecture Overview](Architecture-Overview) | Layered design, package layout, key design decisions |
| [Roadmap](Roadmap) | Phase-by-phase delivery plan with epics and definitions of done |
| [Contributing](Contributing) | Coding conventions, PR process, spec workflow |

---

## At a Glance

```
Resource Definition  ─── defines the schema (type, aspects, facets)
        │
        ▼
    Resource (V1)  ──── created, implicitly draft
        │
        ▼
    Resource (V2)  ──── new version after update
        │
        ▼
  Activation (V2, "Published")  ──── V2 is now live in the Published channel
```

All resource versions are **immutable and append-only**. Activation is a separate concern from creation — any version can be activated in any named channel at any time.

