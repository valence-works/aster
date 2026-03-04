using Aster.Core.Models.Definitions;

namespace Aster.Core.Abstractions;

/// <summary>
/// Manages the registry of <see cref="ResourceDefinition"/> versions.
/// Each registration appends a new immutable version; existing versions are never overwritten.
/// </summary>
public interface IResourceDefinitionStore
{
    /// <summary>
    /// Returns the latest version of the definition, or <see langword="null"/> if not registered.
    /// </summary>
    /// <param name="definitionId">The logical definition identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a specific (<c>DefinitionId</c>, <c>Version</c>) snapshot, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="definitionId">The logical definition identifier.</param>
    /// <param name="version">The specific version number to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Always appends a new immutable version. Auto-increments <see cref="ResourceDefinition.Version"/>.
    /// Never overwrites an existing version.
    /// </summary>
    /// <param name="definition">The definition to register.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest version of each distinct definition ID.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default);
}
