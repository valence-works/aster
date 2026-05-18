namespace Aster.Core.Models.Instances;

/// <summary>
/// Schema-version status for a single resource version snapshot.
/// </summary>
public enum ResourceSchemaStatus
{
    /// <summary>The resource version references the latest available definition version.</summary>
    Current,

    /// <summary>The resource version references an available definition version older than latest.</summary>
    OlderThanLatest,

    /// <summary>No definition exists for the resource's definition identifier.</summary>
    MissingDefinition,

    /// <summary>The resource references a definition version that is not available.</summary>
    MissingDefinitionVersion,

    /// <summary>The resource version does not record definition version lineage.</summary>
    UnknownResourceLineage,
}

/// <summary>
/// Diagnostic result describing schema-version status for one resource version.
/// </summary>
public sealed record ResourceSchemaStatusResult
{
    /// <summary>Gets the logical resource identifier.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Gets the resource version number that was inspected.</summary>
    public required int ResourceVersion { get; init; }

    /// <summary>Gets the resource definition identifier.</summary>
    public required string DefinitionId { get; init; }

    /// <summary>Gets the definition version recorded on the resource version, when known.</summary>
    public int? RecordedDefinitionVersion { get; init; }

    /// <summary>Gets the latest available definition version, when known.</summary>
    public int? LatestDefinitionVersion { get; init; }

    /// <summary>Gets the schema-version status.</summary>
    public required ResourceSchemaStatus Status { get; init; }

    /// <summary>Gets a human-readable status message.</summary>
    public required string Message { get; init; }
}
