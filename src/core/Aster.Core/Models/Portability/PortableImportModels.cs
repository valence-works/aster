namespace Aster.Core.Models.Portability;

/// <summary>
/// Import options for portable snapshots.
/// </summary>
public sealed class PortableImportOptions
{
    /// <summary>
    /// Collision handling mode.
    /// </summary>
    public PortableImportCollisionMode CollisionMode { get; set; } = PortableImportCollisionMode.Strict;
}

/// <summary>
/// Collision handling mode for import.
/// </summary>
public enum PortableImportCollisionMode
{
    /// <summary>
    /// Fail before writing when divergent collisions exist.
    /// </summary>
    Strict,

    /// <summary>
    /// Deterministically remap divergent items.
    /// </summary>
    RemapDivergent,
}

/// <summary>
/// Planned import counts.
/// </summary>
public sealed record PortablePlannedImportCounts
{
    /// <summary>Definition versions planned for import.</summary>
    public int Definitions { get; init; }

    /// <summary>Logical resources planned for import.</summary>
    public int Resources { get; init; }

    /// <summary>Resource versions planned for import.</summary>
    public int ResourceVersions { get; init; }

    /// <summary>Activation entries planned for import.</summary>
    public int ActivationEntries { get; init; }

    /// <summary>Items expected to be reused because existing content is identical.</summary>
    public int ReusedIdenticalItems { get; init; }

    /// <summary>Items expected to be remapped.</summary>
    public int RemappedItems { get; init; }
}

/// <summary>
/// Actual import counts.
/// </summary>
public sealed record PortableActualImportCounts
{
    /// <summary>Definition versions imported.</summary>
    public int Definitions { get; init; }

    /// <summary>Logical resources imported.</summary>
    public int Resources { get; init; }

    /// <summary>Resource versions imported.</summary>
    public int ResourceVersions { get; init; }

    /// <summary>Activation entries imported.</summary>
    public int ActivationEntries { get; init; }

    /// <summary>Items reused because existing content was identical.</summary>
    public int ReusedIdenticalItems { get; init; }

    /// <summary>Items remapped.</summary>
    public int RemappedItems { get; init; }
}

/// <summary>
/// Identity mapping produced by preview or import.
/// </summary>
public sealed record PortableIdentityMapping
{
    /// <summary>
    /// Mapped entity kind.
    /// </summary>
    public required PortableEntityKind EntityKind { get; init; }

    /// <summary>
    /// Source identity from the snapshot.
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Target identity after import planning.
    /// </summary>
    public required string TargetId { get; init; }

    /// <summary>
    /// Reason for this mapping.
    /// </summary>
    public required PortableIdentityMappingReason Reason { get; init; }
}

/// <summary>
/// Portable entity kind.
/// </summary>
public enum PortableEntityKind
{
    /// <summary>Logical definition.</summary>
    Definition,

    /// <summary>Definition version.</summary>
    DefinitionVersion,

    /// <summary>Logical resource.</summary>
    Resource,

    /// <summary>Resource version.</summary>
    ResourceVersion,

    /// <summary>Activation entry.</summary>
    ActivationEntry,
}

/// <summary>
/// Identity mapping reason.
/// </summary>
public enum PortableIdentityMappingReason
{
    /// <summary>Identity is preserved.</summary>
    Preserved,

    /// <summary>Existing identical content is reused.</summary>
    ReusedIdentical,

    /// <summary>Identity was remapped to avoid a divergent collision.</summary>
    RemappedDivergent,
}

/// <summary>
/// Import status.
/// </summary>
public enum PortableImportStatus
{
    /// <summary>Snapshot was imported.</summary>
    Imported,

    /// <summary>Import was a no-op because all content already existed identically.</summary>
    NoOp,

    /// <summary>Import failed before writing.</summary>
    Failed,
}
