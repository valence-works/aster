using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default host-controlled lifecycle restore workflow service.
/// </summary>
public sealed class ResourceLifecycleRestoreService : IResourceLifecycleRestoreService
{
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerClearStore markerStore;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceLifecycleRestoreService"/>.
    /// </summary>
    public ResourceLifecycleRestoreService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerClearStore markerStore)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        this.versionReader = versionReader;
        this.markerStore = markerStore;
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
            Candidates = results,
        };
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

            results[index] = apply
                ? await ApplyCandidateAsync(index, candidate, expectedState, tenant, markers, latestResources, cancellationToken)
                : PreviewCandidate(index, candidate, expectedState, tenant, markers, latestResources);
        }

        return results.Select(static result => result!).ToList();
    }

    private async ValueTask<ResourceLifecycleRestoreCandidateResult> ApplyCandidateAsync(
        int index,
        ResourceLifecycleRestoreCandidate candidate,
        ResourceLifecycleMarkerState expectedState,
        TenantScope tenant,
        IDictionary<string, ResourceLifecycleMarker> markers,
        IReadOnlyDictionary<string, Resource> latestResources,
        CancellationToken cancellationToken)
    {
        if (!latestResources.ContainsKey(candidate.ResourceId!))
            return TargetNotFound(index, candidate, tenant);

        if (!markers.TryGetValue(candidate.ResourceId!, out var marker))
            return CandidateResult(index, candidate, ResourceLifecycleRestoreCandidateStatus.AlreadyRestored);

        if (marker.State != expectedState)
            return MarkerMismatch(index, candidate, expectedState, marker);

        var removed = await markerStore.ClearMarkerAsync(candidate.ResourceId!, tenant, expectedState, cancellationToken);
        if (removed)
        {
            markers.Remove(candidate.ResourceId!);
            return CandidateResult(index, candidate, ResourceLifecycleRestoreCandidateStatus.Restored, marker);
        }

        var current = await markerStore.GetMarkerAsync(candidate.ResourceId!, tenant, cancellationToken);
        if (current is null)
        {
            markers.Remove(candidate.ResourceId!);
            return CandidateResult(index, candidate, ResourceLifecycleRestoreCandidateStatus.AlreadyRestored);
        }

        markers[candidate.ResourceId!] = current;
        return MarkerMismatch(index, candidate, expectedState, current);
    }

    private static ResourceLifecycleRestoreCandidateResult PreviewCandidate(
        int index,
        ResourceLifecycleRestoreCandidate candidate,
        ResourceLifecycleMarkerState expectedState,
        TenantScope tenant,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        IReadOnlyDictionary<string, Resource> latestResources)
    {
        if (!latestResources.ContainsKey(candidate.ResourceId!))
            return TargetNotFound(index, candidate, tenant);

        if (!markers.TryGetValue(candidate.ResourceId!, out var marker))
            return CandidateResult(index, candidate, ResourceLifecycleRestoreCandidateStatus.AlreadyRestored);

        return marker.State == expectedState
            ? CandidateResult(index, candidate, ResourceLifecycleRestoreCandidateStatus.Restorable, marker)
            : MarkerMismatch(index, candidate, expectedState, marker);
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

    private static ResourceLifecycleRestoreCandidateResult TargetNotFound(
        int index,
        ResourceLifecycleRestoreCandidate candidate,
        TenantScope tenant) =>
        Failure(
            index,
            candidate,
            ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound,
            $"Resource '{candidate.ResourceId}' was not found in tenant '{tenant.TenantId}'.",
            "resourceId");

    private static ResourceLifecycleRestoreCandidateResult MarkerMismatch(
        int index,
        ResourceLifecycleRestoreCandidate candidate,
        ResourceLifecycleMarkerState expectedState,
        ResourceLifecycleMarker marker) =>
        Failure(
            index,
            candidate,
            ResourcePolicyDiagnosticCodes.LifecycleRestoreMarkerMismatch,
            $"Resource '{candidate.ResourceId}' is marked as {marker.State}; expected {expectedState}.",
            "expectedState",
            marker);

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
