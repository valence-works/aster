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
    private readonly IResourceLifecycleMarkerTransitionService transitions;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePolicyApplicationService"/>.
    /// </summary>
    /// <param name="definitionStore">Definition store used to validate current policy declarations.</param>
    /// <param name="versionReader">Resource version reader used for tenant-scoped latest-version checks.</param>
    /// <param name="markerStore">Lifecycle marker store used to read existing markers and persist applied lifecycle outcomes.</param>
    public ResourcePolicyApplicationService(
        IResourceDefinitionStore definitionStore,
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore)
        : this(
            definitionStore,
            versionReader,
            markerStore,
            new ResourceLifecycleMarkerTransitionService(versionReader, markerStore))
    {
    }

    internal ResourcePolicyApplicationService(
        IResourceDefinitionStore definitionStore,
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore,
        IResourceLifecycleMarkerTransitionService transitions)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        ArgumentNullException.ThrowIfNull(transitions);
        this.definitionStore = definitionStore;
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        this.transitions = transitions;
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

        var shapeFailures = candidates
            .Select((candidate, index) => ValidateShape(index, candidate))
            .ToArray();
        var candidateResourceIds = candidates
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.ResourceId))
            .Select(static candidate => candidate.ResourceId!)
            .ToHashSet(StringComparer.Ordinal);
        var latestResources = candidateResourceIds.Count == 0
            ? new Dictionary<string, Resource>(StringComparer.Ordinal)
            : (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                TenantScope = tenant,
                Scope = ResourceVersionScope.Latest,
                ResourceIds = candidateResourceIds,
        }, cancellationToken)).ToDictionary(static resource => resource.ResourceId, StringComparer.Ordinal);
        var markers = candidateResourceIds.Count == 0
            ? new Dictionary<string, ResourceLifecycleMarker>(StringComparer.Ordinal)
            : new Dictionary<string, ResourceLifecycleMarker>(
                await markerStore.GetMarkersAsync(candidateResourceIds, tenant, cancellationToken),
                StringComparer.Ordinal);
        var definitions = new Dictionary<string, ResourceDefinition?>(StringComparer.Ordinal);
        var conflictIndexes = FindConflictingLifecycleCandidates(candidates, shapeFailures);
        var results = new ResourcePolicyApplicationCandidateResult?[candidates.Count];
        var processedLifecycleResults = new Dictionary<LifecycleCandidateKey, ResourcePolicyApplicationCandidateResult>();

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];

            if (shapeFailures[index] is not null)
            {
                results[index] = shapeFailures[index];
                continue;
            }

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

            latestResources.TryGetValue(candidate.ResourceId!, out var latest);
            if (latest is not null && candidate.ResourceVersion is { } resourceVersion && latest.Version != resourceVersion)
            {
                results[index] = Failure(
                    index,
                    candidate,
                    ResourcePolicyDiagnosticCodes.PolicyApplicationStaleCandidate,
                    $"Resource '{candidate.ResourceId}' version {resourceVersion} is stale; latest version is {latest.Version}.",
                    "resourceVersion");
                continue;
            }

            if (latest is not null)
            {
                var policyFailure = await ValidatePolicyAsync(index, candidate, latest, tenant, definitions, cancellationToken);
                if (policyFailure is not null)
                {
                    results[index] = policyFailure;
                    continue;
                }
            }

            var key = new LifecycleCandidateKey(candidate.ResourceId!, markerState);
            if (processedLifecycleResults.TryGetValue(key, out var previousResult))
            {
                results[index] = DuplicateResult(index, candidate, markerState, markers, previousResult);
                continue;
            }

            markers.TryGetValue(candidate.ResourceId!, out var existing);
            results[index] = await ApplyMarkerAsync(index, candidate, markerState, tenant, existing, request, markers, cancellationToken);
            processedLifecycleResults[key] = results[index]!;
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
        IDictionary<string, ResourceDefinition?> definitions,
        CancellationToken cancellationToken)
    {
        if (!definitions.TryGetValue(latest.DefinitionId, out var definition))
        {
            definition = await definitionStore.GetDefinitionAsync(latest.DefinitionId, tenant, cancellationToken);
            definitions[latest.DefinitionId] = definition;
        }

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
            var path = policy.Kind != candidate.PolicyKind ? "policyKind" : "outcome";
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationPolicyMismatch,
                $"Policy '{candidate.PolicyId}' no longer matches the submitted kind and outcome.",
                path);
        }

        return null;
    }

    private async ValueTask<ResourcePolicyApplicationCandidateResult> ApplyMarkerAsync(
        int index,
        ResourcePolicyApplicationCandidate candidate,
        ResourceLifecycleMarkerState markerState,
        TenantScope tenant,
        ResourceLifecycleMarker? existing,
        ResourcePolicyApplicationRequest request,
        IDictionary<string, ResourceLifecycleMarker> markers,
        CancellationToken cancellationToken)
    {
        var transition = await transitions.ApplyAsync(new ResourceLifecycleMarkerTransitionApplyRequest
        {
            TenantScope = tenant,
            ResourceId = candidate.ResourceId!,
            State = markerState,
            MarkedAt = request.AppliedAt,
            Reason = candidate.Reason ?? request.Reason,
            HasCurrentMarker = true,
            CurrentMarker = existing,
        }, cancellationToken);

        if (transition.Status == ResourceLifecycleMarkerTransitionStatus.Applied && transition.Marker is not null)
            markers[candidate.ResourceId!] = transition.Marker;

        var status = transition.Status switch
        {
            ResourceLifecycleMarkerTransitionStatus.Applied => ResourcePolicyApplicationCandidateStatus.Applied,
            ResourceLifecycleMarkerTransitionStatus.AlreadySatisfied => ResourcePolicyApplicationCandidateStatus.AlreadySatisfied,
            _ => ResourcePolicyApplicationCandidateStatus.Failed,
        };

        return CandidateResult(index, candidate, status, transition.Marker, transition.Diagnostics);
    }

    private static ResourcePolicyApplicationCandidateResult DuplicateResult(
        int index,
        ResourcePolicyApplicationCandidate candidate,
        ResourceLifecycleMarkerState markerState,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        ResourcePolicyApplicationCandidateResult previousResult)
    {
        if (previousResult.Status == ResourcePolicyApplicationCandidateStatus.Failed)
        {
            return CandidateResult(
                index,
                candidate,
                ResourcePolicyApplicationCandidateStatus.Failed,
                previousResult.Marker,
                previousResult.Diagnostics);
        }

        markers.TryGetValue(candidate.ResourceId!, out var marker);
        return CandidateResult(
            index,
            candidate,
            marker?.State == markerState
                ? ResourcePolicyApplicationCandidateStatus.AlreadySatisfied
                : ResourcePolicyApplicationCandidateStatus.Skipped,
            marker);
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

    private static HashSet<int> FindConflictingLifecycleCandidates(
        IReadOnlyList<ResourcePolicyApplicationCandidate> candidates,
        IReadOnlyList<ResourcePolicyApplicationCandidateResult?> shapeFailures)
    {
        var outcomesByResourceId = new Dictionary<string, HashSet<ResourcePolicyOutcome>>(StringComparer.Ordinal);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (shapeFailures[index] is not null
                || string.IsNullOrWhiteSpace(candidate.ResourceId)
                || candidate.Outcome is not { } outcome
                || !IsLifecycleOutcome(outcome))
            {
                continue;
            }

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
            if (shapeFailures[index] is null
                && candidate.ResourceId is not null
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

    private static ResourcePolicyApplicationCandidateResult CandidateResult(
        int index,
        ResourcePolicyApplicationCandidate candidate,
        ResourcePolicyApplicationCandidateStatus status,
        ResourceLifecycleMarker? marker,
        IReadOnlyList<ResourcePolicyDiagnostic>? diagnostics = null) =>
        new()
        {
            Index = index,
            Status = status,
            PolicyId = candidate.PolicyId,
            Outcome = candidate.Outcome,
            ResourceId = candidate.ResourceId,
            ResourceVersion = candidate.ResourceVersion,
            Marker = marker,
            Diagnostics = diagnostics ?? [],
        };

    private readonly record struct LifecycleCandidateKey(string ResourceId, ResourceLifecycleMarkerState State);
}
