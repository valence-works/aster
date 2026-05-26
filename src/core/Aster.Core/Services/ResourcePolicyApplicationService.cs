using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default host-controlled policy application service.
/// </summary>
public sealed class ResourcePolicyApplicationService : IResourcePolicyApplicationService
{
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore markerStore;
    private readonly IResourceLifecycleMarkerService markerService;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePolicyApplicationService"/>.
    /// </summary>
    /// <param name="definitionStore">Definition store used to validate current policy declarations.</param>
    /// <param name="versionReader">Resource version reader used for tenant-scoped latest-version checks.</param>
    /// <param name="markerStore">Lifecycle marker store used to detect already-satisfied candidates.</param>
    /// <param name="markerService">Lifecycle marker service used to apply supported marker writes.</param>
    public ResourcePolicyApplicationService(
        IResourceDefinitionStore definitionStore,
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore,
        IResourceLifecycleMarkerService markerService)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        ArgumentNullException.ThrowIfNull(markerService);
        this.definitionStore = definitionStore;
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        this.markerService = markerService;
    }

    /// <inheritdoc />
    public async ValueTask<ResourcePolicyApplicationResult> ApplyAsync(
        ResourcePolicyApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var candidates = request.Candidates;
        if (candidates.Count == 0)
        {
            return new ResourcePolicyApplicationResult
            {
                TenantScope = tenant,
                AppliedAt = request.AppliedAt,
                Candidates = [],
            };
        }

        var latestResources = (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.Latest,
        }, cancellationToken)).ToDictionary(static resource => resource.ResourceId, StringComparer.Ordinal);
        var conflictIndexes = FindConflictingLifecycleCandidates(candidates);
        var results = new ResourcePolicyApplicationCandidateResult?[candidates.Count];
        var appliedKeys = new HashSet<LifecycleCandidateKey>();

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];

            if (conflictIndexes.Contains(index))
            {
                results[index] = Failure(
                    index,
                    candidate,
                    ResourcePolicyDiagnosticCodes.PolicyApplicationConflictingOutcome,
                    $"Resource '{candidate.ResourceId}' has conflicting archive and soft-delete outcomes in this request.",
                    "outcome");
                continue;
            }

            var shapeFailure = ValidateShape(index, candidate);
            if (shapeFailure is not null)
            {
                results[index] = shapeFailure;
                continue;
            }

            var outcome = candidate.Outcome!.Value;
            if (outcome == ResourcePolicyOutcome.PrunePreview)
            {
                results[index] = Failure(
                    index,
                    candidate,
                    ResourcePolicyDiagnosticCodes.PolicyPruningPreviewOnly,
                    "Version pruning remains preview-only and was not applied.",
                    "outcome");
                continue;
            }

            if (!TryMapLifecycleState(outcome, out var markerState))
            {
                results[index] = Failure(
                    index,
                    candidate,
                    ResourcePolicyDiagnosticCodes.PolicyApplicationOutcomeUnsupported,
                    $"Policy outcome '{outcome}' is not supported for application.",
                    "outcome");
                continue;
            }

            if (!latestResources.TryGetValue(candidate.ResourceId!, out var latest))
            {
                results[index] = Failure(
                    index,
                    candidate,
                    ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound,
                    $"Resource '{candidate.ResourceId}' was not found in tenant '{tenant.TenantId}'.",
                    "resourceId");
                continue;
            }

            if (candidate.ResourceVersion is { } resourceVersion && latest.Version != resourceVersion)
            {
                results[index] = Failure(
                    index,
                    candidate,
                    ResourcePolicyDiagnosticCodes.PolicyApplicationStaleCandidate,
                    $"Resource '{candidate.ResourceId}' version {resourceVersion} is stale; latest version is {latest.Version}.",
                    "resourceVersion");
                continue;
            }

            var policyFailure = await ValidatePolicyAsync(index, candidate, latest, tenant, cancellationToken);
            if (policyFailure is not null)
            {
                results[index] = policyFailure;
                continue;
            }

            var key = new LifecycleCandidateKey(candidate.ResourceId!, markerState);
            if (!appliedKeys.Add(key))
            {
                var marker = await markerStore.GetMarkerAsync(candidate.ResourceId!, tenant, cancellationToken);
                results[index] = new ResourcePolicyApplicationCandidateResult
                {
                    Index = index,
                    Status = marker?.State == markerState
                        ? ResourcePolicyApplicationCandidateStatus.AlreadySatisfied
                        : ResourcePolicyApplicationCandidateStatus.Skipped,
                    PolicyId = candidate.PolicyId,
                    Outcome = candidate.Outcome,
                    ResourceId = candidate.ResourceId,
                    ResourceVersion = candidate.ResourceVersion,
                    Marker = marker,
                };
                continue;
            }

            var existing = await markerStore.GetMarkerAsync(candidate.ResourceId!, tenant, cancellationToken);
            var markerResult = await markerService.ApplyAsync(new ResourceLifecycleMarkerRequest
            {
                TenantScope = tenant,
                ResourceId = candidate.ResourceId!,
                State = markerState,
                MarkedAt = request.AppliedAt,
                Reason = candidate.Reason ?? request.Reason,
            }, cancellationToken);

            results[index] = markerResult.Diagnostics.Count == 0
                ? new ResourcePolicyApplicationCandidateResult
                {
                    Index = index,
                    Status = existing?.State == markerState
                        ? ResourcePolicyApplicationCandidateStatus.AlreadySatisfied
                        : ResourcePolicyApplicationCandidateStatus.Applied,
                    PolicyId = candidate.PolicyId,
                    Outcome = candidate.Outcome,
                    ResourceId = candidate.ResourceId,
                    ResourceVersion = candidate.ResourceVersion,
                    Marker = markerResult.Marker,
                }
                : new ResourcePolicyApplicationCandidateResult
                {
                    Index = index,
                    Status = ResourcePolicyApplicationCandidateStatus.Failed,
                    PolicyId = candidate.PolicyId,
                    Outcome = candidate.Outcome,
                    ResourceId = candidate.ResourceId,
                    ResourceVersion = candidate.ResourceVersion,
                    Marker = markerResult.Marker,
                    Diagnostics = markerResult.Diagnostics,
                };
        }

        return new ResourcePolicyApplicationResult
        {
            TenantScope = tenant,
            AppliedAt = request.AppliedAt,
            Candidates = results.Select(static result => result!).ToList(),
        };
    }

    private async ValueTask<ResourcePolicyApplicationCandidateResult?> ValidatePolicyAsync(
        int index,
        ResourcePolicyApplicationCandidate candidate,
        Resource latest,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetDefinitionAsync(latest.DefinitionId, tenant, cancellationToken);
        var policy = definition?.PolicyDeclarations.FirstOrDefault(policy =>
            string.Equals(policy.PolicyId, candidate.PolicyId, StringComparison.Ordinal));

        if (policy is null)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationPolicyMissing,
                $"Policy '{candidate.PolicyId}' is not declared on the current definition for resource '{candidate.ResourceId}'.",
                "policyId");
        }

        if (policy.Kind != candidate.PolicyKind || policy.Outcome != candidate.Outcome)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationPolicyMismatch,
                $"Policy '{candidate.PolicyId}' no longer matches the submitted kind and outcome.",
                "outcome");
        }

        return null;
    }

    private static ResourcePolicyApplicationCandidateResult? ValidateShape(
        int index,
        ResourcePolicyApplicationCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.PolicyId))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationCandidateInvalid,
                "Policy application candidates require a policy identifier.",
                "policyId");
        }

        if (string.IsNullOrWhiteSpace(candidate.ResourceId))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationCandidateInvalid,
                "Policy application candidates require a resource identifier.",
                "resourceId");
        }

        if (candidate.PolicyKind is null || !Enum.IsDefined(candidate.PolicyKind.Value))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationCandidateInvalid,
                "Policy application candidates require a supported policy kind.",
                "policyKind");
        }

        if (candidate.Outcome is null || !Enum.IsDefined(candidate.Outcome.Value))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationCandidateInvalid,
                "Policy application candidates require a supported policy outcome.",
                "outcome");
        }

        return null;
    }

    private static HashSet<int> FindConflictingLifecycleCandidates(IReadOnlyList<ResourcePolicyApplicationCandidate> candidates)
    {
        var outcomesByResourceId = new Dictionary<string, HashSet<ResourcePolicyOutcome>>(StringComparer.Ordinal);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (string.IsNullOrWhiteSpace(candidate.ResourceId) || candidate.Outcome is not { } outcome || !IsLifecycleOutcome(outcome))
                continue;

            if (!outcomesByResourceId.TryGetValue(candidate.ResourceId, out var outcomes))
            {
                outcomes = [];
                outcomesByResourceId[candidate.ResourceId] = outcomes;
            }

            outcomes.Add(outcome);
        }

        var conflictingResourceIds = outcomesByResourceId
            .Where(static pair => pair.Value.Count > 1)
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        var indexes = new HashSet<int>();
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.ResourceId is not null
                && conflictingResourceIds.Contains(candidate.ResourceId)
                && candidate.Outcome is { } outcome
                && IsLifecycleOutcome(outcome))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static bool TryMapLifecycleState(ResourcePolicyOutcome outcome, out ResourceLifecycleMarkerState markerState)
    {
        markerState = outcome switch
        {
            ResourcePolicyOutcome.Archive => ResourceLifecycleMarkerState.Archived,
            ResourcePolicyOutcome.SoftDelete => ResourceLifecycleMarkerState.SoftDeleted,
            _ => ResourceLifecycleMarkerState.None,
        };

        return markerState != ResourceLifecycleMarkerState.None;
    }

    private static bool IsLifecycleOutcome(ResourcePolicyOutcome outcome) =>
        outcome is ResourcePolicyOutcome.Archive or ResourcePolicyOutcome.SoftDelete;

    private static ResourcePolicyApplicationCandidateResult Failure(
        int index,
        ResourcePolicyApplicationCandidate candidate,
        string code,
        string message,
        string? path = null) =>
        new()
        {
            Index = index,
            Status = ResourcePolicyApplicationCandidateStatus.Failed,
            PolicyId = candidate.PolicyId,
            Outcome = candidate.Outcome,
            ResourceId = candidate.ResourceId,
            ResourceVersion = candidate.ResourceVersion,
            Diagnostics =
            [
                ResourcePolicyValidator.Diagnostic(
                    code,
                    message,
                    path,
                    candidate.PolicyId,
                    candidate.ResourceId,
                    candidate.ResourceVersion),
            ],
        };

    private readonly record struct LifecycleCandidateKey(string ResourceId, ResourceLifecycleMarkerState State);
}
