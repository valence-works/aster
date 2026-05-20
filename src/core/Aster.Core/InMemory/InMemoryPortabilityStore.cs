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

    /// <inheritdoc />
    public ValueTask<PortableTargetState> ReadTargetStateAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var definitions = new List<ResourceDefinition>();
        foreach (var definition in snapshot.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = definitionStore.GetDefinitionVersion(definition.DefinitionId, definition.Version);
            if (existing is not null)
                definitions.Add(existing);
        }

        var resources = new List<Resource>();
        foreach (var resource in snapshot.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versions = resourceStore.TryGetVersions(resource.ResourceId);
            if (versions is null)
                continue;

            lock (versions)
            {
                var existing = versions.FirstOrDefault(candidate => candidate.Version == resource.Version);
                if (existing is not null)
                    resources.Add(existing);
            }
        }

        var activationStates = new List<ActivationState>();
        foreach (var state in snapshot.ActivationStates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (resourceStore.ActivationStates.TryGetValue(state.ResourceId, out var states)
                && states.TryGetValue(state.Channel, out var existing))
            {
                activationStates.Add(existing);
            }
        }

        return ValueTask.FromResult(new PortableTargetState
        {
            Definitions = definitions,
            Resources = resources,
            ActivationStates = activationStates,
        });
    }

    /// <inheritdoc />
    public async ValueTask ApplyImportAsync(
        PortableSnapshot plannedSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plannedSnapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var appliedDefinitions = new List<ResourceDefinition>();
        var appliedResources = new List<Resource>();
        var appliedActivationStates = new List<(ActivationState State, ActivationState? PreviousState)>();

        try
        {
            foreach (var definition in plannedSnapshot.Definitions
                .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal)
                .ThenBy(static definition => definition.Version))
            {
                cancellationToken.ThrowIfCancellationRequested();
                definitionStore.ImportDefinitionVersion(definition);
                appliedDefinitions.Add(definition);
            }

            foreach (var resource in plannedSnapshot.Resources
                .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
                .ThenBy(static resource => resource.Version))
            {
                cancellationToken.ThrowIfCancellationRequested();
                resourceStore.ImportVersion(resource);
                appliedResources.Add(resource);
            }

            foreach (var state in plannedSnapshot.ActivationStates
                .OrderBy(static state => state.ResourceId, StringComparer.Ordinal)
                .ThenBy(static state => state.Channel, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = resourceStore.GetActivationState(state.ResourceId, state.Channel);
                await resourceStore.UpdateActivationAsync(state.ResourceId, state.Channel, state, cancellationToken);
                appliedActivationStates.Add((state, previous));
            }
        }
        catch
        {
            RollBackAppliedChanges(appliedDefinitions, appliedResources, appliedActivationStates);
            throw;
        }
    }

    private void RollBackAppliedChanges(
        IReadOnlyList<ResourceDefinition> appliedDefinitions,
        IReadOnlyList<Resource> appliedResources,
        IReadOnlyList<(ActivationState State, ActivationState? PreviousState)> appliedActivationStates)
    {
        foreach (var (state, previousState) in appliedActivationStates.Reverse())
            resourceStore.RestoreActivationState(state.ResourceId, state.Channel, previousState);

        foreach (var resource in appliedResources.Reverse())
            resourceStore.RemoveImportedVersion(resource);

        foreach (var definition in appliedDefinitions.Reverse())
            definitionStore.RemoveImportedDefinitionVersion(definition);
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

        foreach (var (resourceId, resourceActivationStates) in resourceStore.ActivationStates.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!includedVersions.TryGetValue(resourceId, out var includedResourceVersions))
                continue;

            var states = resourceActivationStates
                .OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(static item => item.Value)
                .ToList();

            foreach (var state in states)
            {
                var includedActiveVersions = state.ActiveVersions
                    .Where(includedResourceVersions.Contains)
                    .Order()
                    .ToList();

                foreach (var skippedVersion in state.ActiveVersions.Except(includedActiveVersions).Order())
                {
                    skippedEntries.Add(new SkippedActivationEntry
                    {
                        ResourceId = resourceId,
                        Channel = state.Channel,
                        Version = skippedVersion,
                        Reason = SkippedActivationReason.ExcludedByResourceVersionScope,
                    });
                }

                if (includedActiveVersions.Count == 0)
                    continue;

                activationStates.Add(state with { ActiveVersions = includedActiveVersions });
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
