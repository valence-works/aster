namespace Aster.Core.Exceptions;

/// <summary>
/// Thrown when a specific resource version cannot be found.
/// </summary>
public sealed class VersionNotFoundException : Exception
{
    /// <inheritdoc />
    public VersionNotFoundException() : base("The requested resource version was not found.") { }

    /// <inheritdoc />
    public VersionNotFoundException(string resourceId, int version)
        : base($"Version {version} of resource '{resourceId}' was not found.") { }

    /// <inheritdoc />
    public VersionNotFoundException(string message) : base(message) { }

    /// <inheritdoc />
    public VersionNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an optimistic concurrency conflict is detected during an update or activation.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    /// <inheritdoc />
    public ConcurrencyException() : base("A concurrency conflict was detected; the resource has been modified by another operation.") { }

    /// <inheritdoc />
    public ConcurrencyException(string resourceId, int expectedVersion, int actualVersion)
        : base($"Concurrency conflict on resource '{resourceId}': expected version {expectedVersion}, but found version {actualVersion}.") { }

    /// <inheritdoc />
    public ConcurrencyException(string message) : base(message) { }

    /// <inheritdoc />
    public ConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an aspect with the same key is attached to a resource definition more than once.
/// </summary>
public sealed class DuplicateAspectAttachmentException : Exception
{
    /// <inheritdoc />
    public DuplicateAspectAttachmentException() : base("An aspect with the same key is already attached to this resource definition.") { }

    /// <inheritdoc />
    public DuplicateAspectAttachmentException(string key)
        : base($"An aspect with key '{key}' is already attached to this resource definition.") { }

    /// <inheritdoc />
    public DuplicateAspectAttachmentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when a caller supplies a <c>ResourceId</c> during creation that is already in use.
/// </summary>
public sealed class DuplicateResourceIdException : Exception
{
    /// <inheritdoc />
    public DuplicateResourceIdException() : base("A resource with the supplied ID already exists.") { }

    /// <inheritdoc />
    public DuplicateResourceIdException(string resourceId)
        : base($"A resource with ID '{resourceId}' already exists.") { }

    /// <inheritdoc />
    public DuplicateResourceIdException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when attempting to create more than one instance of a singleton resource definition.
/// </summary>
public sealed class SingletonViolationException : Exception
{
    /// <inheritdoc />
    public SingletonViolationException() : base("Only one instance is permitted for this singleton resource definition.") { }

    /// <inheritdoc />
    public SingletonViolationException(string definitionId)
        : base($"An instance of the singleton definition '{definitionId}' already exists.") { }

    /// <inheritdoc />
    public SingletonViolationException(string message, Exception innerException) : base(message, innerException) { }
}
