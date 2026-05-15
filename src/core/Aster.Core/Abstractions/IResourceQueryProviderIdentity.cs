namespace Aster.Core.Abstractions;

/// <summary>
/// Exposes the stable provider key used to match query execution with declared capabilities.
/// </summary>
public interface IResourceQueryProviderIdentity
{
    /// <summary>
    /// Gets the stable provider key for this query provider.
    /// </summary>
    string ProviderKey { get; }
}
