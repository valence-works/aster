namespace Aster.Core.Models.Definitions;

/// <summary>
/// Outcome of a conditional definition registration
/// (<see cref="Abstractions.IResourceDefinitionStore.RegisterDefinitionIfLatestAsync"/>).
/// </summary>
public enum ResourceDefinitionRegistrationStatus
{
    /// <summary>The definition was appended as the new latest version.</summary>
    Registered,

    /// <summary>
    /// Nothing was written: the definition's latest version is no longer the expected one, because another
    /// writer registered a version since the caller read it (or the definition exists when none was expected).
    /// </summary>
    VersionConflict,
}

/// <summary>
/// Result of a conditional definition registration.
/// </summary>
public sealed record ResourceDefinitionRegistrationResult
{
    /// <summary>Whether the definition was registered or conflicted.</summary>
    public required ResourceDefinitionRegistrationStatus Status { get; init; }

    /// <summary>The registered version as stored; <see langword="null"/> on conflict.</summary>
    public ResourceDefinition? Definition { get; init; }

    /// <summary>
    /// The definition's latest version after the call: the new version when registered, or the version that
    /// caused the conflict (<c>0</c> when the definition does not exist).
    /// </summary>
    public required int LatestVersion { get; init; }

    /// <summary><see langword="true"/> when the definition was registered.</summary>
    public bool IsRegistered => Status == ResourceDefinitionRegistrationStatus.Registered;
}
