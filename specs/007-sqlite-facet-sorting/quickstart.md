# Quickstart: SQLite JSON Facet Sorting

Facet sorting is available on SQLite JSON queries by setting `AspectKey` on `SortExpression`:

```csharp
var results = await query.QueryAsync(new ResourceQuery
{
    DefinitionId = "Product",
    Sorts = [new SortExpression("Title", AspectKey: "Title")]
});
```

Verify focused behavior:

```bash
dotnet test test/Aster.Tests/Aster.Tests.csproj --filter "FullyQualifiedName~SqliteJsonQueryServiceTests|FullyQualifiedName~SqliteJsonQueryCapabilityTests|FullyQualifiedName~ProviderConformanceTests"
```

Full verification:

```bash
dotnet test Aster.sln
dotnet build Aster.sln
git diff --check
```
