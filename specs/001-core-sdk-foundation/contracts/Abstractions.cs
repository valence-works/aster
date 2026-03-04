namespace Aster.Core.Abstractions;

public interface IResourceDefinitionStore
{
    ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default);
    ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default);
    ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default);
}

public interface IResourceManager
{
    ValueTask<ResourceVersion> CreateAsync(string definitionId, CreateResourceRequest request, CancellationToken cancellationToken = default);
    ValueTask<ResourceVersion> UpdateAsync(string resourceId, UpdateResourceRequest request, CancellationToken cancellationToken = default);
    ValueTask<ResourceVersion?> GetVersionAsync(string resourceId, int version, CancellationToken cancellationToken = default);
    ValueTask<IEnumerable<ResourceVersion>> GetVersionsAsync(string resourceId, CancellationToken cancellationToken = default);
    ValueTask<ResourceVersion?> GetLatestVersionAsync(string resourceId, CancellationToken cancellationToken = default);
    
    // Activation
    ValueTask ActivateAsync(string resourceId, int version, string channel, bool allowMultipleActive = false, CancellationToken cancellationToken = default);
    ValueTask DeactivateAsync(string resourceId, int version, string channel, CancellationToken cancellationToken = default);
    ValueTask<IEnumerable<ResourceVersion>> GetActiveVersionsAsync(string resourceId, string channel, CancellationToken cancellationToken = default);
}

public class CreateResourceRequest
{
    public Dictionary<string, object> InitialAspects { get; set; } = new();
}

public class UpdateResourceRequest
{
    public int BaseVersion { get; set; } // Optimistic Locking
    public Dictionary<string, object> AspectUpdates { get; set; } = new();
}

// Required by Constitution Principle V (Provider Agnostic).
// InMemoryResourceManager implements this internally; a SQL/Document store would plug in here.
public interface IResourceWriteStore
{
    ValueTask<Resource> CreateResourceAsync(string definitionId, CancellationToken cancellationToken = default);
    ValueTask<ResourceVersion> WriteVersionAsync(string resourceId, ResourceVersion version, CancellationToken cancellationToken = default);
    ValueTask<ActivationState> UpdateActivationAsync(string resourceId, string channel, ActivationState state, CancellationToken cancellationToken = default);
}

