namespace Aster.Core.Models.Portability;

/// <summary>
/// Diagnostic emitted by portability validation, export, preview, or import.
/// </summary>
public sealed record PortableDiagnostic
{
    /// <summary>
    /// Stable machine-readable diagnostic code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Diagnostic severity.
    /// </summary>
    public required PortableDiagnosticSeverity Severity { get; init; }

    /// <summary>
    /// Optional location within the snapshot or request.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Human-readable diagnostic detail.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Diagnostic severity for portability operations.
/// </summary>
public enum PortableDiagnosticSeverity
{
    /// <summary>
    /// Informational diagnostic.
    /// </summary>
    Info,

    /// <summary>
    /// Warning diagnostic.
    /// </summary>
    Warning,

    /// <summary>
    /// Error diagnostic.
    /// </summary>
    Error,
}

/// <summary>
/// Stable portability diagnostic codes.
/// </summary>
public static class PortableDiagnosticCodes
{
    /// <summary>
    /// Export request scope is invalid or incomplete.
    /// </summary>
    public const string InvalidExportScope = "invalid-export-scope";

    /// <summary>
    /// Exported activation entry references a resource version excluded by the requested scope.
    /// </summary>
    public const string SkippedActivationEntry = "skipped-activation-entry";

    /// <summary>
    /// Snapshot format version is not supported.
    /// </summary>
    public const string UnsupportedFormatVersion = "unsupported-format-version";

    /// <summary>
    /// Snapshot references a missing definition version.
    /// </summary>
    public const string MissingDefinitionReference = "missing-definition-reference";

    /// <summary>
    /// Snapshot references a missing resource version.
    /// </summary>
    public const string MissingResourceReference = "missing-resource-reference";

    /// <summary>
    /// Snapshot contains duplicate identities.
    /// </summary>
    public const string DuplicateSnapshotIdentity = "duplicate-snapshot-identity";

    /// <summary>
    /// Snapshot content collides with divergent target content.
    /// </summary>
    public const string DivergentIdentityCollision = "divergent-identity-collision";

    /// <summary>
    /// Import behavior is not implemented yet.
    /// </summary>
    public const string ImportNotImplemented = "import-not-implemented";

    /// <summary>
    /// RemapDivergent import handling is not implemented yet.
    /// </summary>
    public const string RemapDivergentNotImplemented = "remap-divergent-not-implemented";

    /// <summary>
    /// Planned import writes failed during provider apply.
    /// </summary>
    public const string ImportApplyFailed = "import-apply-failed";
}
