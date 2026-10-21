using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Request to explicitly upgrade a resource to a target definition version.
/// </summary>
public sealed class ResourceSchemaUpgradeRequest
{
    /// <summary>
    /// Tenant scope for the schema upgrade. When omitted, the default single-tenant scope is used.
    /// </summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Must match the current latest resource version.
    /// </summary>
    public int BaseVersion { get; set; }

    /// <summary>
    /// Optional target definition version. When omitted, the latest definition version is used.
    /// </summary>
    public int? TargetDefinitionVersion { get; set; }

    /// <summary>
    /// Aspect updates keyed by aspect key. State replace semantics match normal resource updates.
    /// </summary>
    public Dictionary<string, object> AspectUpdates { get; set; } = [];
}

/// <summary>
/// Outcome kind for an explicit schema upgrade request.
/// </summary>
public enum ResourceSchemaUpgradeStatus
{
    /// <summary>A new resource version was appended.</summary>
    Upgraded,

    /// <summary>No resource version was appended because the target matches the source lineage.</summary>
    NoOp,

    /// <summary>
    /// No resource version was appended because the request cannot be applied; the diagnostics say why.
    /// </summary>
    Rejected,
}

/// <summary>
/// Result of an explicit schema upgrade request.
/// </summary>
public sealed record ResourceSchemaUpgradeResult
{
    /// <summary>Gets the upgrade outcome kind.</summary>
    public required ResourceSchemaUpgradeStatus Status { get; init; }

    /// <summary>
    /// Gets the upgraded resource version when one was created, or the unchanged latest version for
    /// <see cref="ResourceSchemaUpgradeStatus.NoOp"/>; <see langword="null"/> when the request was rejected.
    /// </summary>
    public Resource? Resource { get; init; }

    /// <summary>Gets the source definition version, when known.</summary>
    public int? SourceDefinitionVersion { get; init; }

    /// <summary>
    /// Gets the target definition version: the requested one, or the latest definition version when none was
    /// requested. <c>0</c> when the request was rejected before a target could be resolved (the resource or its
    /// definition is missing).
    /// </summary>
    public required int TargetDefinitionVersion { get; init; }

    /// <summary>Gets aspect keys preserved even though the target definition does not declare them.</summary>
    public IReadOnlyList<string> CarriedForwardAspectKeys { get; init; } = [];

    /// <summary>
    /// Gets why the request was rejected, in a deterministic order; empty unless <see cref="Status"/> is
    /// <see cref="ResourceSchemaUpgradeStatus.Rejected"/>. Codes are listed in
    /// <see cref="ResourceSchemaUpgradeDiagnosticCodes"/>, plus the facet value codes in
    /// <see cref="FacetValueDiagnosticCodes"/> when the upgraded payload is invalid.
    /// </summary>
    public IReadOnlyList<ResourceSchemaUpgradeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Gets whether the request was applied: upgraded, or a no-op.</summary>
    public bool Succeeded => Status != ResourceSchemaUpgradeStatus.Rejected;

    /// <summary>Gets a human-readable result message.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Throws a <see cref="ResourceSchemaUpgradeException"/> carrying the diagnostics when the request was rejected,
    /// for callers that prefer exception flow; otherwise returns this result.
    /// </summary>
    /// <returns>This result, when it succeeded.</returns>
    /// <exception cref="ResourceSchemaUpgradeException">The request was rejected.</exception>
    public ResourceSchemaUpgradeResult ThrowIfRejected()
    {
        if (Succeeded)
            return this;

        IReadOnlyList<ResourceSchemaUpgradeDiagnostic> diagnostics = Diagnostics.Count > 0
            ? Diagnostics
            : [new ResourceSchemaUpgradeDiagnostic { Code = "invalid-schema-upgrade", Message = Message }];
        throw new ResourceSchemaUpgradeException(
            diagnostics,
            definitionId: Resource?.DefinitionId,
            targetDefinitionVersion: TargetDefinitionVersion > 0 ? TargetDefinitionVersion : null);
    }
}

/// <summary>
/// What <c>IResourceSchemaVersionService.UpgradeAsync</c> would do with the same request now, computed without
/// writing anything.
/// </summary>
public sealed record ResourceSchemaUpgradePreview
{
    /// <summary>Gets the logical resource identifier.</summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Gets the latest resource version the preview read; <see langword="null"/> when the resource is missing.
    /// </summary>
    public int? ResourceVersion { get; init; }

    /// <summary>Gets the resource's definition identifier, when the resource exists.</summary>
    public string? DefinitionId { get; init; }

    /// <summary>
    /// Gets the outcome the upgrade would report: <see cref="ResourceSchemaUpgradeStatus.Upgraded"/> when it would
    /// append a version, <see cref="ResourceSchemaUpgradeStatus.NoOp"/>, or
    /// <see cref="ResourceSchemaUpgradeStatus.Rejected"/> with the blocking <see cref="Diagnostics"/>.
    /// </summary>
    public required ResourceSchemaUpgradeStatus Status { get; init; }

    /// <summary>Gets the source definition version, when known.</summary>
    public int? SourceDefinitionVersion { get; init; }

    /// <summary>
    /// Gets the target definition version: the requested one, or the latest definition version when none was
    /// requested; <see langword="null"/> when it cannot be resolved.
    /// </summary>
    public int? TargetDefinitionVersion { get; init; }

    /// <summary>
    /// Gets the aspect keys the upgrade would carry forward although the target definition does not declare them.
    /// Reported whenever the target definition version resolves, so a rejected preview still shows them; empty for
    /// a no-op.
    /// </summary>
    public IReadOnlyList<string> CarriedForwardAspectKeys { get; init; } = [];

    /// <summary>Gets the diagnostics that would reject the upgrade, in a deterministic order.</summary>
    public IReadOnlyList<ResourceSchemaUpgradeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Gets whether the upgrade would append a new resource version.</summary>
    public bool CanUpgrade => Status == ResourceSchemaUpgradeStatus.Upgraded;

    /// <summary>Gets a human-readable preview message.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// One reason a schema upgrade request is rejected.
/// </summary>
public sealed record ResourceSchemaUpgradeDiagnostic
{
    /// <summary>
    /// Gets the stable code: one of <see cref="ResourceSchemaUpgradeDiagnosticCodes"/>, or a facet value code from
    /// <see cref="FacetValueDiagnosticCodes"/>.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Gets a human-readable explanation.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the aspect attachment key, for a facet value diagnostic.</summary>
    public string? AspectKey { get; init; }

    /// <summary>Gets the facet definition identifier, for a facet value diagnostic.</summary>
    public string? FacetDefinitionId { get; init; }

    /// <summary>Gets the zero-based array element index, when a facet value diagnostic concerns one element.</summary>
    public int? ElementIndex { get; init; }
}

/// <summary>
/// Stable schema upgrade diagnostic codes. An invalid upgraded payload uses the facet value codes in
/// <see cref="FacetValueDiagnosticCodes"/>.
/// </summary>
public static class ResourceSchemaUpgradeDiagnosticCodes
{
    /// <summary>The resource has no versions in the tenant.</summary>
    public const string ResourceNotFound = "resource-not-found";

    /// <summary>The request's base version is not the latest resource version.</summary>
    public const string ConcurrencyConflict = "concurrency-conflict";

    /// <summary>The resource's definition is not registered in the tenant.</summary>
    public const string MissingDefinition = "missing-definition";

    /// <summary>The target definition version is not registered.</summary>
    public const string MissingDefinitionVersion = "missing-definition-version";

    /// <summary>The target definition version is newer than the latest registered version.</summary>
    public const string TargetDefinitionVersionTooNew = "target-definition-version-too-new";

    /// <summary>The target definition version is older than the resource's recorded definition version.</summary>
    public const string TargetDefinitionVersionBeforeSource = "target-definition-version-before-source";
}
