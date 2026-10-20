using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Explicit shared aspect upgrades (ADR 0003 §6). Preview which resource definitions pin an older version of
/// an aspect and how its facets differ, then re-pin selected definitions, which registers new definition
/// versions. Resources then move through <see cref="IResourceSchemaVersionService"/>; nothing is rewritten
/// automatically.
/// </summary>
public interface IAspectUpgradeService
{
    /// <summary>
    /// Lists the attachments, on each resource definition's latest version, that pin an older version of the
    /// aspect than <paramref name="targetVersion"/>, with facet differences. Non-mutating and deterministic.
    /// </summary>
    /// <param name="aspectDefinitionId">The shared aspect definition ID.</param>
    /// <param name="targetVersion">The target version; <see langword="null"/> means the latest.</param>
    /// <param name="tenantScope">The tenant; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<AspectUpgradePreview> PreviewAsync(
        string aspectDefinitionId,
        int? targetVersion = null,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-pins every older attachment of the aspect on each selected resource definition's latest version to the
    /// target version, registering one new definition version per upgraded definition.
    /// </summary>
    /// <param name="request">The aspect, target version and selected definitions.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<AspectUpgradeResult> ApplyAsync(
        AspectUpgradeRequest request,
        CancellationToken cancellationToken = default);
}
