# Quickstart: Policy Pruning Application

This quickstart shows the intended host workflow for destructive version pruning.

## 1. Declare Version-Pruning Intent

Hosts declare pruning policy metadata on resource definitions.

```csharp
var definition = new ResourceDefinition
{
    DefinitionId = "Product",
    Version = 1,
    PolicyDeclarations =
    [
        new ResourcePolicyDeclaration
        {
            PolicyId = "retain-two-drafts",
            Kind = ResourcePolicyKind.VersionPruning,
            Target = ResourcePolicyTarget.ResourceVersion,
            Outcome = ResourcePolicyOutcome.PrunePreview,
            Criteria = new ResourcePolicyCriteria
            {
                ActivationState = ResourcePolicyActivationState.Draft,
                MaximumRetainedVersions = 2,
            },
        },
    ],
};
```

## 2. Preview Candidate Versions

Policy evaluation remains non-mutating.

```csharp
var preview = await policyEvaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
{
    DefinitionIds = ["Product"],
    PolicyIds = ["retain-two-drafts"],
    EvaluationTimestamp = now,
});

var pruningCandidates = preview.Candidates
    .Where(candidate => candidate.Outcome == ResourcePolicyOutcome.PrunePreview)
    .ToList();
```

## 3. Apply Selected Candidates

Hosts explicitly select candidates to apply. The application service revalidates current state before removing any version.

```csharp
var pruningApplication = serviceProvider.GetRequiredService<IResourcePolicyPruningApplicationService>();

var result = await pruningApplication.ApplyAsync(new ResourcePolicyPruningApplicationRequest
{
    AppliedAt = now,
    Candidates = pruningCandidates
        .Take(2)
        .Select(candidate => new ResourcePolicyPruningApplicationCandidate
        {
            PolicyId = candidate.PolicyId,
            PolicyKind = candidate.PolicyKind,
            Outcome = candidate.Outcome,
            ResourceId = candidate.ResourceId,
            ResourceVersion = candidate.ResourceVersion,
        })
        .ToList(),
});
```

The result contains one entry per submitted candidate:

```csharp
foreach (var candidate in result.Candidates)
{
    Console.WriteLine($"{candidate.ResourceId} v{candidate.ResourceVersion}: {candidate.Status}");
}
```

## Tenant-Scoped Application

Tenant-aware hosts supply tenant scope on preview and application.

```csharp
var tenant = TenantScope.FromTenantId("tenant-a");

var preview = await policyEvaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
{
    TenantScope = tenant,
    DefinitionIds = ["Product"],
    PolicyIds = ["retain-two-drafts"],
});

var result = await pruningApplication.ApplyAsync(new ResourcePolicyPruningApplicationRequest
{
    TenantScope = tenant,
    Candidates = preview.Candidates
        .Where(candidate => candidate.Outcome == ResourcePolicyOutcome.PrunePreview)
        .Select(candidate => new ResourcePolicyPruningApplicationCandidate
        {
            PolicyId = candidate.PolicyId,
            PolicyKind = candidate.PolicyKind,
            Outcome = candidate.Outcome,
            ResourceId = candidate.ResourceId,
            ResourceVersion = candidate.ResourceVersion,
        })
        .ToList(),
});
```

Only versions inside the effective tenant are considered or removed.

## Safety Behavior

Application fails closed when current state no longer matches the preview basis.

Examples:

- The target version became latest.
- The target version became active.
- The policy declaration was removed or changed.
- The version no longer satisfies current policy criteria.
- Removing the version would violate the current retained-version count.
- The active provider cannot remove resource versions.

Failed candidates do not remove versions.

## Retry Behavior

If a candidate version was already removed and the resource still exists in the effective tenant, application returns `AlreadyPruned`.

Duplicate candidates in one request are deterministic:

- First candidate performs or determines the outcome.
- Later duplicates are skipped or already-pruned according to the first result.

## Diagnostics

Hosts can branch on stable diagnostic codes:

- `policy-pruning-candidate-invalid`
- `policy-pruning-target-not-found`
- `policy-pruning-version-protected-latest`
- `policy-pruning-version-protected-active`
- `policy-pruning-policy-missing`
- `policy-pruning-policy-mismatch`
- `policy-pruning-unsafe`
- `policy-pruning-provider-unsupported`
- `policy-pruning-write-failed`

## Non-Goals

This slice does not add automatic policy execution, background schedulers, hidden retention jobs, authorization policies, cross-tenant pruning, runtime scanning, provider registries, provider-specific policy languages, public SQL, public queryable resource surfaces, schema migrations, archive/soft-delete marker mutation, restore behavior changes, or broad workflow/state-machine infrastructure.
