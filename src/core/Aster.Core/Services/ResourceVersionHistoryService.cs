using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default read-only resource version history inspection service.
/// </summary>
public sealed class ResourceVersionHistoryService : IResourceVersionHistoryService
{
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceActivationStateReader activationStateReader;
    private readonly IResourceLifecycleMarkerStore markerStore;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceVersionHistoryService"/>.
    /// </summary>
    public ResourceVersionHistoryService(
        IResourceVersionReader versionReader,
        IResourceActivationStateReader activationStateReader,
        IResourceLifecycleMarkerStore markerStore)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(activationStateReader);
        ArgumentNullException.ThrowIfNull(markerStore);

        this.versionReader = versionReader;
        this.activationStateReader = activationStateReader;
        this.markerStore = markerStore;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceVersionHistoryResult> GetHistoryAsync(
        ResourceVersionHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceId);

        var result = await GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
        {
            TenantScope = request.TenantScope,
            ResourceIds = [request.ResourceId],
        }, cancellationToken);

        return result.Histories[0];
    }

    /// <inheritdoc />
    public async ValueTask<ResourceVersionHistoryBatchResult> GetHistoriesAsync(
        ResourceVersionHistoryBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ResourceIds);

        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var resourceIds = NormalizeResourceIds(request.ResourceIds);

        if (resourceIds.Count == 0)
        {
            return new ResourceVersionHistoryBatchResult
            {
                TenantScope = tenant,
            };
        }

        var versionsByResourceId = (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.AllVersions,
            ResourceIds = resourceIds,
        }, cancellationToken))
            .GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static resource => resource.Version).ToList(),
                StringComparer.Ordinal);

        var activeChannelsByResourceAndVersion = await ReadActiveChannelsByResourceAndVersionAsync(
            resourceIds,
            tenant,
            cancellationToken);
        var markers = await markerStore.GetMarkersAsync(resourceIds, tenant, cancellationToken);

        var histories = resourceIds
            .Select(resourceId =>
            {
                var versions = versionsByResourceId.TryGetValue(resourceId, out var resourceVersions)
                    ? resourceVersions
                    : [];
                var activeChannelsByVersion = activeChannelsByResourceAndVersion.TryGetValue(resourceId, out var resourceActiveChannels)
                    ? resourceActiveChannels
                    : new Dictionary<int, IReadOnlyList<string>>();

                return BuildHistory(
                    resourceId,
                    tenant,
                    versions,
                    activeChannelsByVersion,
                    markers.TryGetValue(resourceId, out var marker) ? marker : null);
            })
            .ToList();

        return new ResourceVersionHistoryBatchResult
        {
            TenantScope = tenant,
            Histories = histories,
        };
    }

    private static IReadOnlyList<string> NormalizeResourceIds(IReadOnlyCollection<string> resourceIds)
    {
        var normalized = new List<string>(resourceIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resourceId in resourceIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

            if (seen.Add(resourceId))
                normalized.Add(resourceId);
        }

        return normalized;
    }

    private static ResourceVersionHistoryResult BuildHistory(
        string resourceId,
        TenantScope tenant,
        IReadOnlyList<Resource> versions,
        IReadOnlyDictionary<int, IReadOnlyList<string>> activeChannelsByVersion,
        ResourceLifecycleMarker? marker)
    {
        if (versions.Count == 0)
        {
            return new ResourceVersionHistoryResult
            {
                TenantScope = tenant,
                ResourceId = resourceId,
            };
        }

        var lifecycleState = marker?.State ?? ResourceLifecycleMarkerState.None;
        var latestVersion = versions[^1].Version;

        var summaries = versions.Select(resource =>
        {
            var activeChannels = activeChannelsByVersion.TryGetValue(resource.Version, out var channels)
                ? channels
                : [];
            var isLatest = resource.Version == latestVersion;
            var isProtected = isLatest || activeChannels.Count > 0;

            return new ResourceVersionSummary
            {
                ResourceVersionId = resource.Id,
                Version = resource.Version,
                DefinitionId = resource.DefinitionId,
                DefinitionVersion = resource.DefinitionVersion,
                Created = resource.Created,
                IsLatest = isLatest,
                IsDraft = activeChannels.Count == 0,
                ActiveChannels = activeChannels,
                LifecycleState = lifecycleState,
                IsProtectedFromPruning = isProtected,
                MaintenanceDisposition = isProtected
                    ? ResourceVersionMaintenanceDisposition.Protected
                    : ResourceVersionMaintenanceDisposition.PossibleCandidate,
            };
        }).ToList();

        return new ResourceVersionHistoryResult
        {
            TenantScope = tenant,
            ResourceId = resourceId,
            Versions = summaries,
        };
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<string>>>> ReadActiveChannelsByResourceAndVersionAsync(
        IReadOnlyCollection<string> resourceIds,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var states = await activationStateReader.ReadActivationStatesAsync(resourceIds, tenant, cancellationToken);
        var channelsByResourceAndVersion = new Dictionary<string, Dictionary<int, SortedSet<string>>>(StringComparer.Ordinal);

        foreach (var state in states)
        {
            if (!channelsByResourceAndVersion.TryGetValue(state.ResourceId, out var channelsByVersion))
            {
                channelsByVersion = new Dictionary<int, SortedSet<string>>();
                channelsByResourceAndVersion[state.ResourceId] = channelsByVersion;
            }

            foreach (var version in state.ActiveVersions)
            {
                if (!channelsByVersion.TryGetValue(version, out var channels))
                {
                    channels = new SortedSet<string>(StringComparer.Ordinal);
                    channelsByVersion[version] = channels;
                }

                channels.Add(state.Channel);
            }
        }

        return channelsByResourceAndVersion.ToDictionary(
            static item => item.Key,
            static item => (IReadOnlyDictionary<int, IReadOnlyList<string>>)item.Value.ToDictionary(
                static versionItem => versionItem.Key,
                static versionItem => (IReadOnlyList<string>)versionItem.Value.ToList()),
            StringComparer.Ordinal);
    }
}
