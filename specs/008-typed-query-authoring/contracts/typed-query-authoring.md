# Contract: Typed Query Authoring Ergonomics

## Public Behavior

Typed query authoring helpers are convenience APIs over the existing portable query AST. They MUST NOT introduce a new provider contract.

## Typed Sort Helpers

Expected behavior:

- A typed facet selection can produce ascending and descending `SortExpression` values.
- Convention-based identifiers match existing typed facet filter helpers:
  - Aspect key defaults to the typed aspect CLR type name.
  - Facet identifier defaults to the selected member name.
- Explicit aspect key and facet identifier overrides are supported.
- Invalid selectors fail clearly before producing a sort.

Equivalent manual shape:

```csharp
new SortExpression("Title", SortDirection.Ascending, AspectKey: "TitleAspect")
```

## Logical Composition Helpers

Expected behavior:

- `And` returns `new LogicalExpression(LogicalOperator.And, operands)`.
- `Or` returns `new LogicalExpression(LogicalOperator.Or, operands)`.
- `Not` returns `new LogicalExpression(LogicalOperator.Not, [operand])`.
- Empty `And`/`Or` inputs are rejected.
- `Not` accepts exactly one operand in its public API.

## Compatibility Requirements

- Existing manual AST construction remains valid.
- Existing validators consume helper-generated AST without special cases.
- Existing providers execute helper-generated queries as equivalent manual queries.
- Public abstractions still do not expose `IQueryable<Resource>`.
