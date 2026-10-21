using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default <see cref="IAspectUpgradeService"/> over the aspect and resource definition stores.
/// </summary>
internal sealed class AspectUpgradeService(
    IAspectDefinitionStore aspectDefinitionStore,
    IResourceDefinitionStore definitionStore) : IAspectUpgradeService
{
    /// <inheritdoc />
    public async ValueTask<AspectUpgradePreview> PreviewAsync(
        string aspectDefinitionId,
        int? targetVersion = null,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var target = await ResolveTargetAsync(aspectDefinitionId, targetVersion, tenant, cancellationToken);
        if (target is null)
        {
            return new AspectUpgradePreview
            {
                TenantScope = tenant,
                AspectDefinitionId = aspectDefinitionId,
                Diagnostics = [NotFound(aspectDefinitionId, targetVersion)],
            };
        }

        var candidates = new List<AspectUpgradeCandidate>();
        var pinnedVersions = new Dictionary<int, AspectDefinition?>();
        foreach (var definition in (await definitionStore.ListDefinitionsAsync(tenant, cancellationToken))
                     .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal))
        {
            foreach (var (key, attachment) in OlderAttachments(definition, target))
            {
                // Diff against the stored pinned version; fall back to the embedded snapshot, which is the same content.
                if (!pinnedVersions.TryGetValue(attachment.Version, out var pinned))
                    pinnedVersions[attachment.Version] = pinned = await aspectDefinitionStore.GetVersionAsync(aspectDefinitionId, attachment.Version, tenant, cancellationToken);

                candidates.Add(new AspectUpgradeCandidate(
                    definition.DefinitionId,
                    definition.Version,
                    key,
                    attachment.Version,
                    Diff(pinned ?? attachment, target)));
            }
        }

        return new AspectUpgradePreview
        {
            TenantScope = tenant,
            AspectDefinitionId = aspectDefinitionId,
            TargetVersion = target.Version,
            Candidates = candidates,
        };
    }

    /// <inheritdoc />
    public async ValueTask<AspectUpgradeResult> ApplyAsync(
        AspectUpgradeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AspectDefinitionId);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);

        var target = await ResolveTargetAsync(request.AspectDefinitionId, request.TargetVersion, tenant, cancellationToken);
        if (target is null)
        {
            return new AspectUpgradeResult
            {
                TenantScope = tenant,
                Diagnostics = [NotFound(request.AspectDefinitionId, request.TargetVersion)],
            };
        }

        var outcomes = new List<AspectUpgradeOutcome>();
        foreach (var definitionId in request.DefinitionIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await ApplyOneAsync(definitionId, target, tenant, cancellationToken));
        }

        return new AspectUpgradeResult
        {
            TenantScope = tenant,
            TargetVersion = target.Version,
            Outcomes = outcomes,
        };
    }

    private async ValueTask<AspectUpgradeOutcome> ApplyOneAsync(
        string definitionId,
        AspectDefinition target,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var latest = await definitionStore.GetDefinitionAsync(definitionId, tenant, cancellationToken);
        var older = latest is null ? [] : OlderAttachments(latest, target).ToList();
        if (latest is null || older.Count == 0)
        {
            return new AspectUpgradeOutcome(definitionId, AspectUpgradeStatus.NotApplicable, null, [], [
                new SharedAspectDiagnostic(
                    SharedAspectDiagnosticCodes.AspectUpgradeNotApplicable,
                    $"Resource definition '{definitionId}' does not pin an older version of aspect '{target.AspectDefinitionId}'.",
                    string.Empty,
                    target.AspectDefinitionId,
                    target.Version),
            ]);
        }

        var keys = older.Select(static attachment => attachment.Key).Order(StringComparer.Ordinal).ToList();

        // A target version that requires a name cannot replace an unnamed attachment (ADR 0003 §4).
        var unnamed = target.RequiresName ? keys.Where(key => !key.Contains(':', StringComparison.Ordinal)).ToList() : [];
        if (unnamed.Count > 0)
        {
            return new AspectUpgradeOutcome(definitionId, AspectUpgradeStatus.Failed, null, keys, unnamed
                .Select(key => new SharedAspectDiagnostic(
                    SharedAspectDiagnosticCodes.AspectNameRequired,
                    $"Aspect definition '{target.AspectDefinitionId}' version {target.Version} requires a named attachment.",
                    key,
                    target.AspectDefinitionId,
                    target.Version))
                .ToList());
        }

        var aspects = new Dictionary<string, AspectDefinition>(latest.AspectDefinitions, StringComparer.Ordinal);
        foreach (var key in keys)
            aspects[key] = target with { IsShared = true };

        // Conditional on the version read above, so a definition another writer changed meanwhile is never
        // superseded by a copy of its older version.
        var registration = await definitionStore.RegisterDefinitionIfLatestAsync(
            latest with { Id = Guid.NewGuid().ToString(), AspectDefinitions = aspects },
            latest.Version,
            tenant,
            cancellationToken);
        if (!registration.IsRegistered)
        {
            return new AspectUpgradeOutcome(definitionId, AspectUpgradeStatus.Failed, null, keys, [
                new SharedAspectDiagnostic(
                    SharedAspectDiagnosticCodes.DefinitionVersionConflict,
                    $"Resource definition '{definitionId}' changed from version {latest.Version} to {registration.LatestVersion} during the upgrade; nothing was written. Preview again and retry.",
                    string.Empty,
                    target.AspectDefinitionId,
                    target.Version),
            ]);
        }

        return new AspectUpgradeOutcome(definitionId, AspectUpgradeStatus.Upgraded, registration.LatestVersion, keys, []);
    }

    private async ValueTask<AspectDefinition?> ResolveTargetAsync(
        string aspectDefinitionId,
        int? targetVersion,
        TenantScope tenant,
        CancellationToken cancellationToken) =>
        targetVersion is { } version
            ? await aspectDefinitionStore.GetVersionAsync(aspectDefinitionId, version, tenant, cancellationToken)
            : await aspectDefinitionStore.GetLatestAsync(aspectDefinitionId, tenant, cancellationToken);

    private static IEnumerable<(string Key, AspectDefinition Aspect)> OlderAttachments(ResourceDefinition definition, AspectDefinition target) =>
        definition.AspectDefinitions
            .Where(pair => pair.Value.IsShared
                && string.Equals(pair.Value.AspectDefinitionId, target.AspectDefinitionId, StringComparison.Ordinal)
                && pair.Value.Version < target.Version)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => (pair.Key, pair.Value));

    /// <summary>
    /// Facet differences, ordered by facet ID (ordinal), then change kind.
    /// </summary>
    internal static IReadOnlyList<AspectFacetChange> Diff(AspectDefinition from, AspectDefinition to)
    {
        var before = from.FacetDefinitions.ToDictionary(static facet => facet.FacetDefinitionId, StringComparer.Ordinal);
        var after = to.FacetDefinitions.ToDictionary(static facet => facet.FacetDefinitionId, StringComparer.Ordinal);
        var changes = new List<AspectFacetChange>();

        foreach (var id in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasBefore = before.TryGetValue(id, out var old);
            var hasAfter = after.TryGetValue(id, out var current);
            if (!hasBefore)
            {
                changes.Add(new AspectFacetChange(id, AspectFacetChangeKind.Added, null, current!.Type));
                continue;
            }

            if (!hasAfter)
            {
                changes.Add(new AspectFacetChange(id, AspectFacetChangeKind.Removed, old!.Type, null));
                continue;
            }

            if (!string.Equals(old!.Type, current!.Type, StringComparison.Ordinal))
                changes.Add(new AspectFacetChange(id, AspectFacetChangeKind.TypeChanged, old.Type, current.Type));

            if (old.IsRequired != current.IsRequired)
                changes.Add(new AspectFacetChange(id, AspectFacetChangeKind.RequiredChanged, Bool(old.IsRequired), Bool(current.IsRequired)));

            var oldMetadata = Metadata(old.Metadata);
            var newMetadata = Metadata(current.Metadata);
            if (!string.Equals(oldMetadata, newMetadata, StringComparison.Ordinal))
                changes.Add(new AspectFacetChange(id, AspectFacetChangeKind.MetadataChanged, oldMetadata, newMetadata));
        }

        return changes;
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string? Metadata(FacetMetadata? metadata) =>
        metadata is null ? null : JsonSerializer.Serialize(metadata);

    private static SharedAspectDiagnostic NotFound(string aspectDefinitionId, int? version) =>
        new(
            SharedAspectDiagnosticCodes.AspectDefinitionNotFound,
            version is { } missing
                ? $"Aspect definition '{aspectDefinitionId}' version {missing} does not exist."
                : $"Aspect definition '{aspectDefinitionId}' does not exist.",
            string.Empty,
            aspectDefinitionId,
            version);
}
