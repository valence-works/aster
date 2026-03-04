using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;

namespace Aster.Core.Abstractions;

/// <summary>
/// Manages the lifecycle of resource instances: creation, versioning, and channel activation.
/// </summary>
/// <remarks>
/// All <c>resourceId</c> parameters refer to <c>Resource.ResourceId</c> (logical persistent identifier).
/// </remarks>
public interface IResourceManager
{
    /// <summary>
    /// Creates V1 of a resource. Resolves <c>ResourceId</c> via <see cref="IIdentityGenerator"/>
    /// unless <see cref="CreateResourceRequest.ResourceId"/> is supplied.
    /// </summary>
    /// <param name="definitionId">The logical definition identifier.</param>
    /// <param name="request">Creation request DTO.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The newly created V1 resource snapshot.</returns>
    /// <exception cref="DuplicateResourceIdException">
    /// Thrown if a caller-supplied <c>ResourceId</c> already exists.
    /// </exception>
    /// <exception cref="SingletonViolationException">
    /// Thrown if the definition is a singleton and an instance already exists.
    /// </exception>
    ValueTask<Resource> CreateAsync(string definitionId, CreateResourceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a new version. Requires <see cref="UpdateResourceRequest.BaseVersion"/> to match the current latest
    /// (optimistic lock).
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="request">Update request DTO.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The newly created version snapshot.</returns>
    /// <exception cref="ConcurrencyException">
    /// Thrown if the store's latest version has changed since the caller last read.
    /// </exception>
    ValueTask<Resource> UpdateAsync(string resourceId, UpdateResourceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a specific version of the resource, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="version">The version number to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<Resource?> GetVersionAsync(string resourceId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all versions of the specified resource.
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<IEnumerable<Resource>> GetVersionsAsync(string resourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest version of the specified resource, or <see langword="null"/> if it does not exist.
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<Resource?> GetLatestVersionAsync(string resourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the given version in the specified channel.
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="version">The version number to activate.</param>
    /// <param name="channel">The channel name (e.g., "Published").</param>
    /// <param name="allowMultipleActive">
    /// If <see langword="false"/> (default), activating a version deactivates all others in the same channel.
    /// If <see langword="true"/>, the version is added alongside existing active versions.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="VersionNotFoundException">Thrown if the specified version does not exist.</exception>
    /// <exception cref="ConcurrencyException">
    /// Thrown if the store's latest version has changed since the caller last read.
    /// </exception>
    ValueTask ActivateAsync(string resourceId, int version, string channel, bool allowMultipleActive = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the given version in the specified channel.
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="version">The version number to deactivate.</param>
    /// <param name="channel">The channel name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask DeactivateAsync(string resourceId, int version, string channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all active resource versions in the specified channel.
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="channel">The channel name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(string resourceId, string channel, CancellationToken cancellationToken = default);
}
