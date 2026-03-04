using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;

namespace Aster.Core.Abstractions;

/// <summary>
/// Provider-agnostic write store for resource versions and activation state.
/// Required by Constitution Principle V (Provider Agnostic).
/// </summary>
/// <remarks>
/// <c>InMemoryResourceManager</c> implements this contract internally.
/// A SQL or Document store provider would plug in here.
/// <para>
/// <c>Resource</c> IS a version snapshot; there is no separate <c>ResourceVersion</c> type.
/// </para>
/// </remarks>
public interface IResourceWriteStore
{
    /// <summary>
    /// Persists a <see cref="Resource"/> version (V1 on create, V2+ on update).
    /// </summary>
    /// <param name="resource">The resource version to persist.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The persisted resource version.</returns>
    /// <exception cref="SingletonViolationException">
    /// Thrown on V1 if <c>IsSingleton</c> and an instance already exists for the definition.
    /// </exception>
    ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists an updated <see cref="ActivationState"/> for a resource channel.
    /// </summary>
    /// <param name="resourceId">Logical resource identifier.</param>
    /// <param name="channel">The channel name.</param>
    /// <param name="state">The updated activation state.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The persisted activation state.</returns>
    ValueTask<ActivationState> UpdateActivationAsync(string resourceId, string channel, ActivationState state, CancellationToken cancellationToken = default);
}
