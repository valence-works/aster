using Aster.Core.Models.Querying;

namespace Aster.Core.Abstractions;

/// <summary>
/// Exposes the query capabilities of an active resource query provider.
/// </summary>
public interface IResourceQueryCapabilitiesProvider
{
    /// <summary>
    /// Gets the provider's supported query surface.
    /// </summary>
    QueryCapabilityDescription Capabilities { get; }
}
