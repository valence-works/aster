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
internal sealed class ResourcePolicyApplicationService : IResourcePolicyApplicationService
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
        var activationKeys = new ActivationKeys(versionReader, tenant, candidateResourceIds);
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
                var policyFailure = await ValidatePolicyAsync(
                    index,
                    candidate,
                    latest,
                    markerState,
                    tenant,
                    request.AppliedAt,
                    markers,
                    definitions,
                    activationKeys,
                    cancellationToken);
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
            results[index] = await ApplyMarkerAsync(
                index,
                candidate,
                markerState,
                targetExists: latest is not null,
                tenant,
                existing,
                request,
                markers,
                cancellationToken);
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
        ResourceLifecycleMarkerState outcomeState,
        TenantScope tenant,
        DateTimeOffset appliedAt,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        IDictionary<string, ResourceDefinition?> definitions,
        ActivationKeys activationKeys,
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

        // A resource already in the outcome's state needs nothing, whatever has changed since: the transition
        // reports it AlreadySatisfied, so retries stay idempotent.
        if (markers.TryGetValue(latest.ResourceId, out var existing) && existing.State == outcomeState)
            return null;

        // The submitted candidate is only a claim: the resource must still match the policy's criteria now, as the
        // preview evaluates them, or any declared policy could archive any resource.
        var mismatch = await MismatchedCriterionAsync(policy, latest, appliedAt, markers, activationKeys, cancellationToken);
        if (mismatch is not null)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyApplicationPolicyMismatch,
                $"Resource '{candidate.ResourceId}' version {latest.Version} no longer matches policy '{candidate.PolicyId}'.",
                mismatch);
        }

        return null;
    }

    // Returns the path of the first criterion the latest version fails, or null when it matches every one.
    private static async ValueTask<string?> MismatchedCriterionAsync(
        ResourcePolicyDeclaration policy,
        Resource latest,
        DateTimeOffset appliedAt,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        ActivationKeys activationKeys,
        CancellationToken cancellationToken)
    {
        var criteria = policy.Criteria;
        if (!string.IsNullOrWhiteSpace(criteria.UnsupportedFacetPredicate))
            return "criteria";

        if (criteria.MinimumAge is { } minimumAge
            && appliedAt - new DateTimeOffset(DateTime.SpecifyKind(latest.Created, DateTimeKind.Utc)) < minimumAge)
            return "criteria/minimumAge";

        if (criteria.ActivationState is { } activationState)
        {
            var keys = activationState == ResourcePolicyActivationState.Draft
                ? await activationKeys.DraftAsync(cancellationToken)
                : await activationKeys.ActiveAsync(criteria.ActivationChannel!, cancellationToken);
            if (!keys.Contains((latest.ResourceId, latest.Version)))
                return "criteria/activationState";
        }

        if (criteria.LifecycleState is { } expected)
        {
            var actual = markers.TryGetValue(latest.ResourceId, out var marker) ? marker.State : ResourceLifecycleMarkerState.None;
            if (actual != expected)
                return "criteria/lifecycleState";
        }

        return null;
    }

    private async ValueTask<ResourcePolicyApplicationCandidateResult> ApplyMarkerAsync(
        int index,
        ResourcePolicyApplicationCandidate candidate,
        ResourceLifecycleMarkerState markerState,
        bool targetExists,
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
            TargetExists = targetExists,
            HasTargetExistence = true,
            TargetNotFoundDiagnosticPath = "resourceId",
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

    /// <summary>
    /// The candidates' draft versions and per-channel active versions, each read once per request when a policy
    /// first needs them.
    /// </summary>
    private sealed class ActivationKeys(IResourceVersionReader versionReader, TenantScope tenant, IReadOnlyCollection<string> resourceIds)
    {
        private readonly Dictionary<string, HashSet<(string, int)>> activeByChannel = new(StringComparer.Ordinal);
        private HashSet<(string, int)>? draft;

        public async ValueTask<HashSet<(string, int)>> DraftAsync(CancellationToken cancellationToken) =>
            draft ??= await ReadAsync(ResourceVersionScope.Draft, null, cancellationToken);

        public async ValueTask<HashSet<(string, int)>> ActiveAsync(string channel, CancellationToken cancellationToken)
        {
            if (!activeByChannel.TryGetValue(channel, out var keys))
                activeByChannel[channel] = keys = await ReadAsync(ResourceVersionScope.Active, channel, cancellationToken);

            return keys;
        }

        private async ValueTask<HashSet<(string, int)>> ReadAsync(ResourceVersionScope scope, string? channel, CancellationToken cancellationToken) =>
            (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                TenantScope = tenant,
                Scope = scope,
                ActivationChannel = channel,
                ResourceIds = resourceIds,
            }, cancellationToken))
            .Select(static resource => (resource.ResourceId, resource.Version))
            .ToHashSet();
    }
}
