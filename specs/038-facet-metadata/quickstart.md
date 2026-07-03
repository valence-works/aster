# Quickstart: Facet Validation and UI Metadata

## Declare Metadata on a Facet

```csharp
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithTypedAspect<PriceAspect>()
    .WithTypedFacet<decimal>()
    .WithFacetMetadata(new FacetMetadata
    {
        Label = "Price",
        Description = "Retail price in the store currency.",
        DisplayOrder = 10,
        Minimum = 0m,
        Maximum = 10_000m,
        DefaultValue = 0m,
        UiHint = "number",
    })
    .Build();

await definitionStore.RegisterDefinitionAsync(definition);
// Invalid metadata throws FacetMetadataValidationException with ordered
// facet-metadata-* diagnostics identifying the aspect key and facet id.
```

Facets that declare no metadata behave exactly as before. Hosts read `FacetDefinition.Metadata` back from the definition store to generate forms; `DefaultValue` is metadata only and is never injected into saved versions.

## Save-Time Enforcement

```csharp
try
{
    await resourceManager.CreateAsync("Product", request);
}
catch (FacetValueValidationException exception)
{
    // exception.Diagnostics: ordered facet-value-* codes with AspectKey +
    // FacetDefinitionId for mapping failures to form fields. Nothing was persisted.
}
```

## Focused Validation

Run the new facet metadata coverage:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~FacetMetadata|FullyQualifiedName~FacetValueValidation"
```

Run the SQLite parity fixture:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonFacetMetadataTests"
```

## Full Validation

```bash
dotnet build Aster.sln /m:1
dotnet test Aster.sln
```

## Expected Result

- Definitions with valid metadata register and read back with every declared property preserved; undeclared properties are absent.
- Every invalid metadata combination is rejected at registration with its documented `facet-metadata-*` code, all violations reported together in ordinal (aspect key, facet id, code) order.
- Every facet value violation fails the save with its documented `facet-value-*` code, nothing is persisted, and boundary values pass.
- Definitions and saves without metadata, and pre-existing tests, behave exactly as before.
- Portable snapshot export/import round-trips metadata losslessly; the in-memory store and SQLite JSON provider produce identical outcomes and codes with zero SQLite schema changes.
