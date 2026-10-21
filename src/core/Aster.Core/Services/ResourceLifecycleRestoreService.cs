using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default host-controlled lifecycle restore workflow service.
/// </summary>
internal sealed class ResourceLifecycleRestoreService : IResourceLifecycleRestoreService
{
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerClearStore markerStore;
    private readonly IResourceLifecycleMarkerTransitionService transitions;
    private readonly IResourceDefinitionStore? definitionStore;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceLifecycleRestoreService"/>.
    /// </summary>
    public ResourceLifecycleRestoreService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerClearStore markerStore)
        : this(
            versionReader,
            markerStore,
            new ResourceLifecycleMarkerTransitionService(versionReader, markerStore))
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceLifecycleRestoreService"/> whose previews report
    /// inbound references to restorable resources (<see cref="ResourcePolicyDiagnosticCodes.HasInboundReferences"/>).
    /// </summary>
    public ResourceLifecycleRestoreService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerClearStore markerStore,
        IResourceDefinitionStore definitionStore)
        : this(
            versionReader,
            markerStore,
            new ResourceLifecycleMarkerTransitionService(versionReader, markerStore),
            definitionStore)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
    }

    internal ResourceLifecycleRestoreService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerClearStore markerStore,
        IResourceLifecycleMarkerTransitionService transitions,
        IResourceDefinitionStore? definitionStore = null)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        ArgumentNullException.ThrowIfNull(transitions);
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        this.transitions = transitions;
        this.definitionStore = definitionStore;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceLifecycleRestorePreviewResult> PreviewRestoreAsync(
        ResourceLifecycleRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var results = await EvaluateAsync(
            ResolveCandidates(request),
            tenant,
            apply: false,
            cancellationToken);

        return new ResourceLifecycleRestorePreviewResult
        {
            TenantScope = tenant,
            Candidates = await AddInboundReferencesAsync(results, tenant, cancellationToken),
        };
    }

    /// <summary>
    /// Adds a <see cref="ResourcePolicyDiagnosticCodes.HasInboundReferences"/> diagnostic per referrer to each
    /// restorable candidate (#90). Reads latest versions and definitions only when a candidate is restorable.
    /// </summary>
    private async ValueTask<IReadOnlyList<ResourceLifecycleRestoreCandidateResult>> AddInboundReferencesAsync(
        IReadOnlyList<ResourceLifecycleRestoreCandidateResult> results,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var targets = results
            .Where(static result => result.Status == ResourceLifecycleRestoreCandidateStatus.Restorable && result.ResourceId is not null)
            .Select(static result => result.ResourceId!)
            .ToHashSet(StringComparer.Ordinal);
        if (definitionStore is null || targets.Count == 0)
            return results;

        var referrers = InboundReferences.Find(
            await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                TenantScope = tenant,
                Scope = ResourceVersionScope.Latest,
            }, cancellationToken),
            await definitionStore.ListDefinitionsAsync(tenant, cancellationToken),
            targets);

        return results
            .Select(result => result.Status == ResourceLifecycleRestoreCandidateStatus.Restorable
                && result.ResourceId is { } resourceId
                && referrers.TryGetValue(resourceId, out var ids)
                    ? result with { Diagnostics = [.. result.Diagnostics, .. InboundReferences.Diagnostics(resourceId, ids)] }
                    : result)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<ResourceLifecycleRestoreApplicationResult> RestoreAsync(
        ResourceLifecycleRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var results = await EvaluateAsync(
            ResolveCandidates(request),
            tenant,
            apply: true,
            cancellationToken);

        return new ResourceLifecycleRestoreApplicationResult
        {
            TenantScope = tenant,
            RestoredAt = request.RestoredAt,
            Candidates = results,
        };
    }

    private static IReadOnlyList<ResourceLifecycleRestoreCandidate> ResolveCandidates(ResourceLifecycleRestoreRequest request) =>
        request.Candidates ?? [];

    private async ValueTask<IReadOnlyList<ResourceLifecycleRestoreCandidateResult>> EvaluateAsync(
        IReadOnlyList<ResourceLifecycleRestoreCandidate> candidates,
        TenantScope tenant,
        bool apply,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return [];

        var shapeFailures = candidates
            .Select((candidate, index) => ValidateShape(index, candidate))
            .ToArray();
        var resourceIds = candidates
            .Where((candidate, index) => shapeFailures[index] is null && !string.IsNullOrWhiteSpace(candidate.ResourceId))
            .Select(static candidate => candidate!.ResourceId!)
            .ToHashSet(StringComparer.Ordinal);
        var latestResources = resourceIds.Count == 0
            ? new Dictionary<string, Resource>(StringComparer.Ordinal)
            : (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                TenantScope = tenant,
                Scope = ResourceVersionScope.Latest,
                ResourceIds = resourceIds,
            }, cancellationToken)).ToDictionary(static resource => resource.ResourceId, StringComparer.Ordinal);
        var markers = resourceIds.Count == 0
            ? new Dictionary<string, ResourceLifecycleMarker>(StringComparer.Ordinal)
            : new Dictionary<string, ResourceLifecycleMarker>(
                await markerStore.GetMarkersAsync(resourceIds, tenant, cancellationToken),
                StringComparer.Ordinal);
        var results = new ResourceLifecycleRestoreCandidateResult?[candidates.Count];
        var processed = new HashSet<RestoreCandidateKey>();

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];

            if (shapeFailures[index] is not null)
            {
                results[index] = shapeFailures[index];
                continue;
            }

            var expectedState = candidate!.ExpectedState!.Value;
            var key = new RestoreCandidateKey(candidate.ResourceId!, expectedState);
            if (!processed.Add(key))
            {
                results[index] = CandidateResult(
                    index,
                    candidate,
                    ResourceLifecycleRestoreCandidateStatus.Skipped,
                    marker: markers.GetValueOrDefault(candidate.ResourceId!));
                continue;
            }

            results[index] = await EvaluateCandidateAsync(
                index,
                candidate,
                expectedState,
                tenant,
                apply,
                markers,
                latestResources.ContainsKey(candidate.ResourceId!),
                cancellationToken);
        }

        return results.Select(static result => result!).ToList();
    }

    private async ValueTask<ResourceLifecycleRestoreCandidateResult> EvaluateCandidateAsync(
        int index,
        ResourceLifecycleRestoreCandidate candidate,
        ResourceLifecycleMarkerState expectedState,
        TenantScope tenant,
        bool apply,
        IDictionary<string, ResourceLifecycleMarker> markers,
        bool targetExists,
        CancellationToken cancellationToken)
    {
        markers.TryGetValue(candidate.ResourceId!, out var marker);
        var transition = await transitions.ClearAsync(new ResourceLifecycleMarkerTransitionClearRequest
        {
            TenantScope = tenant,
            ResourceId = candidate.ResourceId!,
            ExpectedState = expectedState,
            Apply = apply,
            TargetExists = targetExists,
            HasTargetExistence = true,
            TargetNotFoundDiagnosticPath = "resourceId",
            CurrentMarker = marker,
            HasCurrentMarker = true,
        }, cancellationToken);

        if (transition.Status is ResourceLifecycleMarkerTransitionStatus.Cleared
            or ResourceLifecycleMarkerTransitionStatus.AlreadyCleared)
        {
            markers.Remove(candidate.ResourceId!);
        }
        else if (transition.Status == ResourceLifecycleMarkerTransitionStatus.MarkerMismatch && transition.Marker is not null)
        {
            markers[candidate.ResourceId!] = transition.Marker;
        }

        var status = transition.Status switch
        {
            ResourceLifecycleMarkerTransitionStatus.ReadyToClear => ResourceLifecycleRestoreCandidateStatus.Restorable,
            ResourceLifecycleMarkerTransitionStatus.Cleared => ResourceLifecycleRestoreCandidateStatus.Restored,
            ResourceLifecycleMarkerTransitionStatus.AlreadyCleared => ResourceLifecycleRestoreCandidateStatus.AlreadyRestored,
            _ => ResourceLifecycleRestoreCandidateStatus.Failed,
        };

        return CandidateResult(index, candidate, status, transition.Marker, transition.Diagnostics);
    }

    private static ResourceLifecycleRestoreCandidateResult? ValidateShape(
        int index,
        ResourceLifecycleRestoreCandidate? candidate)
    {
        if (candidate is null)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid,
                "Lifecycle restore candidates must not be null.",
                "candidate");
        }

        if (string.IsNullOrWhiteSpace(candidate.ResourceId))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid,
                "Lifecycle restore candidates require a resource identifier.",
                "resourceId");
        }

        if (candidate.ExpectedState is null)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid,
                "Lifecycle restore candidates require an expected lifecycle marker state.",
                "expectedState");
        }

        if (!Enum.IsDefined(candidate.ExpectedState.Value)
            || candidate.ExpectedState.Value is not (ResourceLifecycleMarkerState.Archived or ResourceLifecycleMarkerState.SoftDeleted))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.LifecycleRestoreStateUnsupported,
                $"Lifecycle restore expected state '{candidate.ExpectedState.Value}' is not supported.",
                "expectedState");
        }

        return null;
    }

    private static ResourceLifecycleRestoreCandidateResult Failure(
        int index,
        ResourceLifecycleRestoreCandidate? candidate,
        string code,
        string message,
        string? path = null,
        ResourceLifecycleMarker? marker = null) =>
        CandidateResult(
            index,
            candidate,
            ResourceLifecycleRestoreCandidateStatus.Failed,
            marker,
            [
                ResourcePolicyValidator.Diagnostic(
                    code,
                    message,
                    path,
                    resourceId: candidate?.ResourceId),
            ]);

    private static ResourceLifecycleRestoreCandidateResult CandidateResult(
        int index,
        ResourceLifecycleRestoreCandidate? candidate,
        ResourceLifecycleRestoreCandidateStatus status,
        ResourceLifecycleMarker? marker = null,
        IReadOnlyList<ResourcePolicyDiagnostic>? diagnostics = null) =>
        new()
        {
            Index = index,
            ResourceId = candidate?.ResourceId,
            ExpectedState = candidate?.ExpectedState,
            Status = status,
            Marker = marker,
            Diagnostics = diagnostics ?? [],
        };

    private readonly record struct RestoreCandidateKey(string ResourceId, ResourceLifecycleMarkerState ExpectedState);
}
