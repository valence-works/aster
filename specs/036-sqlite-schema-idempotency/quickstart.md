# Quickstart: SQLite Schema Idempotency Hardening

## Focused Validation

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonSchemaIdempotencyTests"
```

## Compatibility Validation

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonTenantScopeTests|FullyQualifiedName~SqliteJsonResourceStoreTests"
dotnet test Aster.sln
dotnet build Aster.sln /m:1
```

## Expected Outcome

- Repeated SQLite initialization preserves persisted state.
- Legacy pre-tenant upgrade reruns do not duplicate rows.
- No legacy bootstrap tables remain after initialization.
- `InitializeSchema = false` remains side-effect-free for provider identity/capability resolution.
