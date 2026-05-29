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

        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var resourceId = request.ResourceId;

        var versions = (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.AllVersions,
            ResourceIds = [resourceId],
        }, cancellationToken)).OrderBy(static resource => resource.Version).ToList();

        if (versions.Count == 0)
        {
            return new ResourceVersionHistoryResult
            {
                TenantScope = tenant,
                ResourceId = resourceId,
            };
        }

        var activeChannelsByVersion = await ReadActiveChannelsByVersionAsync(resourceId, tenant, cancellationToken);
        var marker = await markerStore.GetMarkerAsync(resourceId, tenant, cancellationToken);
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

    private async Task<Dictionary<int, IReadOnlyList<string>>> ReadActiveChannelsByVersionAsync(
        string resourceId,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var states = await activationStateReader.ReadActivationStatesAsync([resourceId], tenant, cancellationToken);
        var channelsByVersion = new Dictionary<int, SortedSet<string>>();

        foreach (var state in states)
        {
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

        return channelsByVersion.ToDictionary(
            static item => item.Key,
            static item => (IReadOnlyList<string>)item.Value.ToList());
    }
}
