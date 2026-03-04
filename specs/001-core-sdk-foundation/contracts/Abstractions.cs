namespace Aster.Core.Abstractions;

public interface IResourceDefinitionStore
{
    /// <summary>Returns the latest version of the definition, or null if not registered.</summary>
    ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default);
    /// <summary>Returns a specific (Id, Version) pair, or null if not found.</summary>
    ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default);
    /// <summary>
    /// Always appends a new immutable version. Auto-increments ResourceDefinition.Version.
    /// Never overwrites an existing version.
    /// </summary>
    ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default);
    /// <summary>Returns the latest version of each distinct definition Id.</summary>
    ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default);
}

// resourceId parameters below refer to Resource.ResourceId (logical persistent identifier).
public interface IResourceManager
{
    /// <summary>Creates V1. Resolves ResourceId via IIdentityGenerator unless CreateResourceRequest.Id is supplied.</summary>
    ValueTask<Resource> CreateAsync(string definitionId, CreateResourceRequest request, CancellationToken cancellationToken = default);
    /// <summary>Appends a new version. Requires BaseVersion to match current latest (optimistic lock).</summary>
    ValueTask<Resource> UpdateAsync(string resourceId, UpdateResourceRequest request, CancellationToken cancellationToken = default);
    ValueTask<Resource?> GetVersionAsync(string resourceId, int version, CancellationToken cancellationToken = default);
    ValueTask<IEnumerable<Resource>> GetVersionsAsync(string resourceId, CancellationToken cancellationToken = default);
    ValueTask<Resource?> GetLatestVersionAsync(string resourceId, CancellationToken cancellationToken = default);

    // Activation
    ValueTask ActivateAsync(string resourceId, int version, string channel, bool allowMultipleActive = false, CancellationToken cancellationToken = default);
    ValueTask DeactivateAsync(string resourceId, int version, string channel, CancellationToken cancellationToken = default);
    ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(string resourceId, string channel, CancellationToken cancellationToken = default);
}

// Pluggable identity generation. Default implementation: GuidIdentityGenerator (Guid.NewGuid().ToString()).
// Register via DI; IResourceManager takes it as a constructor dependency.
public interface IIdentityGenerator
{
    string NewId();
}

public class CreateResourceRequest
{
    /// <summary>
    /// Optional caller-supplied resource ID. When null or empty the engine delegates to IIdentityGenerator.
    /// If supplied and already in use, CreateAsync throws DuplicateResourceIdException.
    /// </summary>
    public string? Id { get; set; }
    public Dictionary<string, object> InitialAspects { get; set; } = new();
}

public class UpdateResourceRequest
{
    public int BaseVersion { get; set; } // Optimistic Locking
    public Dictionary<string, object> AspectUpdates { get; set; } = new();
}

// Required by Constitution Principle V (Provider Agnostic).
// InMemoryResourceManager implements this internally; a SQL/Document store would plug in here.
// Throws SingletonViolationException if definition.IsSingleton and an instance already exists.
// Resource IS a version snapshot; there is no separate ResourceVersion type.
public interface IResourceWriteStore
{
    /// <summary>
    /// Persists a Resource version (V1 on create, V2+ on update).
    /// Implementors must enforce IsSingleton on V1 (throw SingletonViolationException).
    /// </summary>
    ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default);
    ValueTask<ActivationState> UpdateActivationAsync(string resourceId, string channel, ActivationState state, CancellationToken cancellationToken = default);
}

