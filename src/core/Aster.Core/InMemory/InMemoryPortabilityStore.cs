using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;

namespace Aster.Core.InMemory;

/// <summary>
/// In-memory portability snapshot reader over the existing definition and resource stores.
/// </summary>
public sealed class InMemoryPortabilityStore : IResourcePortabilityStore
{
    private readonly InMemoryResourceDefinitionStore definitionStore;
    private readonly InMemoryResourceStore resourceStore;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryPortabilityStore"/>.
    /// </summary>
    public InMemoryPortabilityStore(
        InMemoryResourceDefinitionStore definitionStore,
        InMemoryResourceStore resourceStore)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(resourceStore);

        this.definitionStore = definitionStore;
        this.resourceStore = resourceStore;
    }

    /// <inheritdoc />
    public ValueTask<PortableStoreSnapshot> ReadSnapshotAsync(
        PortableStoreReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resources = SelectResources(request.ExportRequest, cancellationToken);
        var definitions = SelectDefinitions(request.ExportRequest, resources, cancellationToken);
        var (activationStates, skippedActivationEntries) = SelectActivationStates(resources, cancellationToken);

        return ValueTask.FromResult(new PortableStoreSnapshot
        {
            Definitions = definitions,
            Resources = resources,
            ActivationStates = activationStates,
            SkippedActivationEntries = skippedActivationEntries,
        });
    }

    private List<ResourceDefinition> SelectDefinitions(
        PortableSnapshotExportRequest request,
        IReadOnlyCollection<Resource> resources,
        CancellationToken cancellationToken)
    {
        var definitionVersions = new Dictionary<(string DefinitionId, int Version), ResourceDefinition>();

        if (request.ScopeMode is PortableExportScopeMode.DefinitionsOnly or PortableExportScopeMode.DefinitionWithResources)
        {
            foreach (var definitionId in request.DefinitionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var definition in definitionStore.GetDefinitionVersions(definitionId))
                    definitionVersions[(definition.DefinitionId, definition.Version)] = definition;
            }
        }

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (resource.DefinitionVersion is null)
                continue;

            var definition = definitionStore.GetDefinitionVersion(resource.DefinitionId, resource.DefinitionVersion.Value);
            if (definition is not null)
                definitionVersions[(definition.DefinitionId, definition.Version)] = definition;
        }

        return definitionVersions.Values
            .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Version)
            .ToList();
    }

    private List<Resource> SelectResources(
        PortableSnapshotExportRequest request,
        CancellationToken cancellationToken)
    {
        var resourceIds = request.ScopeMode switch
        {
            PortableExportScopeMode.DefinitionsOnly => [],
            PortableExportScopeMode.SelectedResources => request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions
                ? request.SpecificResourceVersions.Select(static reference => reference.ResourceId).ToHashSet(StringComparer.Ordinal)
                : request.ResourceIds,
            PortableExportScopeMode.DefinitionWithResources => request.DefinitionIds
                .SelectMany(resourceStore.GetResourceIdsForDefinition)
                .ToHashSet(StringComparer.Ordinal),
            _ => [],
        };

        var specificVersions = request.SpecificResourceVersions.ToHashSet();
        var resources = new List<Resource>();

        foreach (var resourceId in resourceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versions = resourceStore.TryGetVersions(resourceId);
            if (versions is null)
                continue;

            List<Resource> snapshot;
            lock (versions)
                snapshot = [.. versions];

            resources.AddRange(SelectVersions(snapshot, request.ResourceVersionScope, specificVersions));
        }

        return resources
            .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ThenBy(static resource => resource.Version)
            .ToList();
    }

    private (List<ActivationState> ActivationStates, List<SkippedActivationEntry> SkippedActivationEntries) SelectActivationStates(
        IReadOnlyCollection<Resource> resources,
        CancellationToken cancellationToken)
    {
        var includedVersions = resources
            .GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static resource => resource.Version).ToHashSet(),
                StringComparer.Ordinal);

        var activationStates = new List<ActivationState>();
        var skippedEntries = new List<SkippedActivationEntry>();

        foreach (var (resourceId, channelActivations) in resourceStore.Activations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!includedVersions.TryGetValue(resourceId, out var includedResourceVersions))
                continue;

            Dictionary<string, HashSet<int>> channels;
            lock (channelActivations)
                channels = channelActivations.ToDictionary(static item => item.Key, static item => item.Value.ToHashSet(), StringComparer.Ordinal);

            foreach (var (channel, activeVersions) in channels.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                var includedActiveVersions = activeVersions
                    .Where(includedResourceVersions.Contains)
                    .Order()
                    .ToList();

                foreach (var skippedVersion in activeVersions.Except(includedActiveVersions).Order())
                {
                    skippedEntries.Add(new SkippedActivationEntry
                    {
                        ResourceId = resourceId,
                        Channel = channel,
                        Version = skippedVersion,
                        Reason = SkippedActivationReason.ExcludedByResourceVersionScope,
                    });
                }

                if (includedActiveVersions.Count == 0)
                    continue;

                activationStates.Add(new ActivationState
                {
                    ResourceId = resourceId,
                    Channel = channel,
                    ActiveVersions = includedActiveVersions,
                    LastUpdated = DateTime.UtcNow,
                });
            }
        }

        return (activationStates, skippedEntries);
    }

    private static IEnumerable<Resource> SelectVersions(
        IReadOnlyList<Resource> versions,
        PortableResourceVersionScope versionScope,
        IReadOnlySet<ResourceVersionReference> specificVersions) =>
        versionScope switch
        {
            PortableResourceVersionScope.AllVersions => versions,
            PortableResourceVersionScope.LatestOnly => versions.Count > 0 ? [versions[^1]] : [],
            PortableResourceVersionScope.SpecificVersions => versions
                .Where(version => specificVersions.Contains(new ResourceVersionReference
                {
                    ResourceId = version.ResourceId,
                    Version = version.Version,
                })),
            _ => [],
        };
}
