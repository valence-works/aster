# Quickstart: Operational Hardening

Run the focused hardening tests:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~OperationalHardeningTests"
```

Run the full validation suite:

```bash
dotnet test Aster.sln
dotnet build Aster.sln /m:1
git diff --check
```

Expected outcome:

- Restore retries leave markers cleared.
- Pruning retries leave only the selected version removed.
- Repeated historical activation leaves active channels unique and latest versions unchanged.
- No new storage, provider, scheduler, query, or dependency behavior is introduced.
