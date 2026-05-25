# Quickstart: Policy Foundations

This quickstart shows the intended SDK behavior for explicit policy declarations, deterministic previews, lifecycle marker writes, and lifecycle-state queries.

## Declare Policies On A Resource Definition

Policy declarations are resource definition metadata. Registering the definition appends a normal immutable definition version.

```csharp
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithPolicy(new ResourcePolicyDeclaration
    {
        PolicyId = "archive-old-products",
        Name = "Archive old products",
        Kind = ResourcePolicyKind.Archival,
        Target = ResourcePolicyTarget.Resource,
        Outcome = ResourcePolicyOutcome.Archive,
        Criteria = new ResourcePolicyCriteria
        {
            MinimumAge = TimeSpan.FromDays(365),
            LifecycleState = ResourceLifecycleMarkerState.None,
        },
    })
    .WithPolicy(new ResourcePolicyDeclaration
    {
        PolicyId = "keep-last-three-versions",
        Name = "Keep last three versions",
        Kind = ResourcePolicyKind.VersionPruning,
        Target = ResourcePolicyTarget.ResourceVersion,
        Outcome = ResourcePolicyOutcome.PrunePreview,
        Criteria = new ResourcePolicyCriteria
        {
            MaximumRetainedVersions = 3,
        },
    })
    .Build();

await definitionStore.RegisterDefinitionAsync(definition);
```

Policy declarations do not execute automatically.

## Validate Policy Declarations

Hosts can validate policy metadata before preview or execution decisions.

```csharp
var policyValidator = serviceProvider.GetRequiredService<IResourcePolicyValidator>();
var result = policyValidator.Validate(definition);

if (!result.IsValid)
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
    }
}
```

Validation does not mutate resource data.

## Preview Policy Outcomes

Policy previews are deterministic. Age-based criteria require an explicit evaluation timestamp.

```csharp
var policyEvaluation = serviceProvider.GetRequiredService<IResourcePolicyEvaluationService>();
var preview = await policyEvaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
{
    DefinitionIds = ["Product"],
    EvaluationTimestamp = new DateTimeOffset(2026, 05, 25, 12, 00, 00, TimeSpan.Zero),
});

foreach (var candidate in preview.Candidates)
{
    Console.WriteLine($"{candidate.Outcome}: {candidate.ResourceId} ({candidate.Reason})");
}
```

Preview does not archive, soft-delete, prune, deactivate, delete, or otherwise mutate resources.

## Apply Archive Or Soft-Delete Markers Explicitly

Hosts apply archive and soft-delete markers through explicit operations.

```csharp
var lifecycleMarkers = serviceProvider.GetRequiredService<IResourceLifecycleMarkerService>();
var markerResult = await lifecycleMarkers.ApplyAsync(new ResourceLifecycleMarkerRequest
{
    ResourceId = "product-1",
    State = ResourceLifecycleMarkerState.Archived,
    MarkedAt = new DateTimeOffset(2026, 05, 25, 12, 00, 00, TimeSpan.Zero),
    Reason = "Archived after policy preview.",
});
```

Applying the same marker again is idempotent. Applying soft-delete to an archived resource, or archive to a soft-deleted resource, returns a stable conflict diagnostic and leaves the existing marker unchanged.

Marker writes do not rewrite resource versions, deactivate active versions, or physically delete data.

## Query Lifecycle State Explicitly

Lifecycle markers are not hidden filters. Hosts query them intentionally.

```csharp
var queryService = serviceProvider.GetRequiredService<IResourceQueryService>();
var archived = await queryService.QueryAsync(new ResourceQuery
{
    DefinitionId = "Product",
    LifecycleState = ResourceLifecycleMarkerState.Archived,
    Scope = ResourceVersionScope.Latest,
});
```

Omitting `LifecycleState` does not implicitly exclude archived or soft-deleted resources.

Activation-state criteria use the same explicit activation model as queries:

```csharp
var activeOnlyPolicy = new ResourcePolicyDeclaration
{
    PolicyId = "archive-active-products",
    Kind = ResourcePolicyKind.Archival,
    Target = ResourcePolicyTarget.Resource,
    Outcome = ResourcePolicyOutcome.Archive,
    Criteria = new ResourcePolicyCriteria
    {
        ActivationState = ResourcePolicyActivationState.Active,
        ActivationChannel = "Published",
        LifecycleState = ResourceLifecycleMarkerState.None,
    },
};
```

## Tenant-Scoped Policy Preview

Tenant-scoped hosts supply tenant scope on the preview request.

```csharp
var tenantA = TenantScope.FromTenantId("tenant-a");

var tenantPreview = await policyEvaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
{
    TenantScope = tenantA,
    DefinitionIds = ["Product"],
    EvaluationTimestamp = new DateTimeOffset(2026, 05, 25, 12, 00, 00, TimeSpan.Zero),
});
```

Preview results include only resources inside the effective tenant boundary.

## Portability

Policy declarations travel with exported definition versions. Lifecycle markers travel with exported resources.

```csharp
var export = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    ScopeMode = PortableExportScopeMode.DefinitionWithResources,
    DefinitionIds = ["Product"],
    ResourceVersionScope = PortableResourceVersionScope.AllVersions,
});

var snapshot = export.Snapshot!;
```

Import preview reports policy and lifecycle marker conflicts without applying writes.

## Failure Cases

Unsupported policy criteria fail closed:

```csharp
var invalid = new ResourcePolicyDeclaration
{
    PolicyId = "facet-policy",
    Kind = ResourcePolicyKind.Archival,
    Target = ResourcePolicyTarget.Resource,
    Outcome = ResourcePolicyOutcome.Archive,
    Criteria = new ResourcePolicyCriteria
    {
        UnsupportedFacetPredicate = "Color = 'Red'",
    },
};
```

Expected diagnostic codes include:

- `policy-invalid`
- `policy-kind-unsupported`
- `policy-outcome-unsupported`
- `policy-target-invalid`
- `policy-criteria-unsupported`
- `policy-conflict`
- `policy-evaluation-timestamp-required`
- `policy-pruning-unsafe`
- `lifecycle-marker-conflict`
- `lifecycle-marker-target-not-found`

`policy-pruning-preview-only` is reserved for a future write-path enforcement surface; this slice has no pruning write API.

## Exclusions

This slice does not add automatic policy execution, background schedulers, hidden retention jobs, authorization policies, cross-tenant policy evaluation, runtime scanning, provider registries, arbitrary facet predicate policy criteria, provider-specific policy languages, public SQL, public `IQueryable<Resource>`, destructive pruning writes, or restore workflows.
