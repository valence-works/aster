namespace Aster.Core.Abstractions;

/// <summary>
/// Pluggable identity generation service.
/// Default implementation: <see cref="Aster.Core.Services.GuidIdentityGenerator"/>.
/// </summary>
public interface IIdentityGenerator
{
    /// <summary>
    /// Generates a new unique identifier string.
    /// </summary>
    /// <returns>A new unique identifier.</returns>
    string NewId();
}
