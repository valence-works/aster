namespace Aster.Core.Models.Definitions;

/// <summary>
/// Outcome of a conditional aspect definition registration.
/// </summary>
public enum AspectDefinitionRegistrationStatus
{
    /// <summary>The aspect was appended as the new latest version.</summary>
    Registered,

    /// <summary>
    /// Nothing was written: the aspect's latest version is no longer the expected one, because another writer
    /// registered a version since the caller read it (or the aspect exists when none was expected).
    /// </summary>
    VersionConflict,
}

/// <summary>
/// Result of a conditional aspect definition registration.
/// </summary>
public sealed record AspectDefinitionRegistrationResult
{
    /// <summary>Whether the aspect was registered or conflicted.</summary>
    public required AspectDefinitionRegistrationStatus Status { get; init; }

    /// <summary>The registered version as stored; <see langword="null"/> on conflict.</summary>
    public AspectDefinition? Aspect { get; init; }

    /// <summary>
    /// The aspect's latest version after the call: the new version when registered, or the version that caused the
    /// conflict (<c>0</c> when the aspect does not exist).
    /// </summary>
    public required int LatestVersion { get; init; }

    /// <summary><see langword="true"/> when the aspect was registered.</summary>
    public bool IsRegistered => Status == AspectDefinitionRegistrationStatus.Registered;
}
