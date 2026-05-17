# Quickstart: Provider Conformance Tests

Provider conformance tests live in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`.

To add a provider to the shared conformance suite:

1. Create a provider subject with an explicit service provider and expected provider key.
2. Seed enough fixture data for the capability areas the provider declares as supported.
3. Run `ProviderConformanceSuite.AssertConformsAsync(subject)`.
4. Keep provider-specific edge cases in dedicated provider tests.

Run the focused checks:

```bash
dotnet test test/Aster.Tests/Aster.Tests.csproj --filter FullyQualifiedName~ProviderConformanceTests
```

Run full verification:

```bash
dotnet test Aster.sln
dotnet build Aster.sln
git diff --check
```
