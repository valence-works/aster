# Quickstart: Policy Application Orchestration

This quickstart shows the intended SDK behavior for explicitly applying selected archive and soft-delete policy preview candidates.

## Preview Policies First

Policy application starts from explicit host selection. A preview remains non-mutating.

```csharp
var preview = await policyEvaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
{
    DefinitionIds = ["Product"],
    EvaluationTimestamp = new DateTimeOffset(2026, 05, 27, 12, 00, 00, TimeSpan.Zero),
});

var selected = preview.Candidates
    .Where(candidate => candidate.Outcome is ResourcePolicyOutcome.Archive or ResourcePolicyOutcome.SoftDelete)
    .ToList();
```

## Apply Selected Lifecycle Candidates

Hosts submit selected candidates to the application service. The service applies only supported lifecycle outcomes.

```csharp
var application = serviceProvider.GetRequiredService<IResourcePolicyApplicationService>();

var result = await application.ApplyAsync(new ResourcePolicyApplicationRequest
{
    AppliedAt = new DateTimeOffset(2026, 05, 27, 12, 30, 00, TimeSpan.Zero),
    Reason = "Applied after operator review.",
    Candidates = selected.Select(candidate => new ResourcePolicyApplicationCandidate
    {
        PolicyId = candidate.PolicyId,
        PolicyKind = candidate.PolicyKind,
        Outcome = candidate.Outcome,
        ResourceId = candidate.ResourceId,
        ResourceVersion = candidate.ResourceVersion,
    }).ToList(),
});
```

Application writes archive and soft-delete lifecycle markers through existing lifecycle marker behavior. It does not prune versions, deactivate active versions, rewrite resources, or change policy declarations.

## Inspect Per-Candidate Results

Every input candidate receives a result.

```csharp
foreach (var candidate in result.Candidates)
{
    Console.WriteLine($"{candidate.Status}: {candidate.ResourceId}");

    foreach (var diagnostic in candidate.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}
```

Expected statuses:

- `Applied`
- `AlreadySatisfied`
- `Skipped`
- `Failed`

## Retry Safely

Reapplying the same archive or soft-delete candidate is idempotent when the marker already exists.

```csharp
var retry = await application.ApplyAsync(previousRequest);
```

The retry reports already-satisfied candidates rather than creating duplicate marker state.

## Stale Candidate Protection

If a candidate includes `ResourceVersion`, that version must still be the latest version for the resource in the effective tenant. A stale candidate fails and does not write a marker.

```csharp
Assert.Contains(result.Candidates, candidate =>
    candidate.Status == ResourcePolicyApplicationCandidateStatus.Failed
    && candidate.Diagnostics.Any(diagnostic => diagnostic.Code == "policy-application-stale-candidate"));
```

## Policy Declaration Protection

The referenced policy declaration must still exist on the current resource definition and still match the requested lifecycle outcome. Missing or mismatched policies fail closed.

## Conflicting Outcomes

If one request includes archive and soft-delete candidates for the same resource, all conflicting candidates for that resource fail before either marker is applied.

## Pruning Remains Preview-Only

Version pruning candidates are rejected with `policy-pruning-preview-only`.

```csharp
Assert.Contains(result.Candidates, candidate =>
    candidate.Diagnostics.Any(diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.PolicyPruningPreviewOnly));
```

## Tenant Scope

Application is tenant-scoped. Omitted tenant scope resolves to the default single-tenant scope.

```csharp
var tenant = TenantScope.FromTenantId("tenant-a");

var tenantResult = await application.ApplyAsync(new ResourcePolicyApplicationRequest
{
    TenantScope = tenant,
    AppliedAt = new DateTimeOffset(2026, 05, 27, 12, 30, 00, TimeSpan.Zero),
    Candidates = selectedCandidates,
});
```

Resources outside the effective tenant are not marked.

## Exclusions

This slice does not add automatic policy execution, background schedulers, hidden retention jobs, authorization policy engines, lifecycle hook behavior, runtime scanning, provider registries, public SQL, public `IQueryable<Resource>`, destructive pruning writes, restore workflows, or provider-specific application executors.
