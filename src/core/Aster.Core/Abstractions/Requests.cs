namespace Aster.Core.Abstractions;

/// <summary>
/// Request DTO for creating a new resource.
/// </summary>
public sealed class CreateResourceRequest
{
    /// <summary>
    /// Optional caller-supplied logical resource ID (<c>ResourceId</c>).
    /// When <see langword="null"/> or empty, the engine delegates to <see cref="IIdentityGenerator"/>.
    /// </summary>
    /// <remarks>
    /// If supplied and already in use, <c>CreateAsync</c> throws
    /// <see cref="Aster.Core.Exceptions.DuplicateResourceIdException"/>.
    /// </remarks>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Initial aspect data keyed by aspect key
    /// (<c>AspectDefinitionId</c> or <c>"{AspectDefinitionId}:{Name}"</c>).
    /// </summary>
    public Dictionary<string, object> InitialAspects { get; set; } = [];
}

/// <summary>
/// Request DTO for updating an existing resource, producing a new immutable version.
/// </summary>
public sealed class UpdateResourceRequest
{
    /// <summary>
    /// Optimistic lock token. Must match the current latest <c>Resource.Version</c>.
    /// </summary>
    /// <exception cref="Aster.Core.Exceptions.ConcurrencyException">
    /// Thrown by <c>UpdateAsync</c> if the store's latest version has changed since the caller last read.
    /// </exception>
    public int BaseVersion { get; set; }

    /// <summary>
    /// Aspect updates keyed by aspect key. State Replace semantics: each entry replaces the full aspect value.
    /// </summary>
    public Dictionary<string, object> AspectUpdates { get; set; } = [];
}
