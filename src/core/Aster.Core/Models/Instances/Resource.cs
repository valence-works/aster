namespace Aster.Core.Models.Instances;

/// <summary>
/// A single immutable version snapshot of a resource. <c>Resource</c> represents both identity
/// and snapshot — there is no separate <c>ResourceVersion</c> type.
/// </summary>
/// <remarks>
/// Universal versioning pattern: <c>ResourceId</c> is the stable logical identifier (persistent across versions);
/// <c>Id</c> uniquely identifies this exact version snapshot.
/// Status is derived: absence from all <see cref="ActivationState.ActiveVersions"/> = draft;
/// presence in a channel's <c>ActiveVersions</c> = active.
/// </remarks>
public sealed record Resource
{
    /// <summary>
    /// Logical persistent identifier. Shared across all versions. Assigned at V1 by
    /// <see cref="Aster.Core.Abstractions.IIdentityGenerator"/> or supplied by the caller.
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Version-specific unique identifier (GUID). Uniquely identifies this exact resource version.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Logical definition identifier (<c>ResourceDefinition.DefinitionId</c>).
    /// </summary>
    public required string DefinitionId { get; init; }

    /// <summary>
    /// Definition version active at creation time (optional, for traceability).
    /// </summary>
    public int? DefinitionVersion { get; init; }

    /// <summary>
    /// Version number (1, 2, 3...).
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Creation timestamp for this specific version.
    /// </summary>
    public required DateTime Created { get; init; }

    /// <summary>
    /// Creator identity; set on V1 and carried forward on subsequent versions.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Aspect data keyed by aspect key:
    /// <c>AspectDefinitionId</c> (unnamed) or <c>"{AspectDefinitionId}:{Name}"</c> (named).
    /// Values are the serialized aspect data.
    /// </summary>
    public IReadOnlyDictionary<string, object> Aspects { get; init; }
        = new Dictionary<string, object>();

    /// <summary>
    /// Optional checksum for integrity verification.
    /// </summary>
    public string? Hash { get; init; }
}
