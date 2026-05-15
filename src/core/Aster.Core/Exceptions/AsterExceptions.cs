using Aster.Core.Models.Querying;

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

/// <summary>
/// Thrown when a query contains a predicate, scope, sort, or value shape that the provider cannot execute.
/// </summary>
public sealed class UnsupportedQueryFeatureException : Exception
{
    /// <summary>
    /// Gets the stable unsupported query failure code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the unsupported query feature category.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// Gets the optional query path where the unsupported feature was found.
    /// </summary>
    public string? Path { get; }

    /// <inheritdoc />
    public UnsupportedQueryFeatureException()
        : this("unsupported-query-feature", "query", "The query contains an unsupported feature.") { }

    /// <inheritdoc />
    public UnsupportedQueryFeatureException(string feature)
        : this("unsupported-query-feature", feature, $"The query feature '{feature}' is not supported.") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedQueryFeatureException"/> class.
    /// </summary>
    /// <param name="code">Stable unsupported query failure code.</param>
    /// <param name="feature">Unsupported query feature category.</param>
    /// <param name="message">Human-readable actionable explanation.</param>
    /// <param name="path">Optional query path where the unsupported feature was found.</param>
    public UnsupportedQueryFeatureException(
        string code,
        string feature,
        string message,
        string? path = null)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Feature = feature;
        Path = path;
    }

    /// <inheritdoc />
    public UnsupportedQueryFeatureException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "unsupported-query-feature";
        Feature = "query";
    }

    /// <summary>
    /// Creates an execution exception from a validation failure.
    /// </summary>
    /// <param name="failure">The validation failure to expose as an execution failure.</param>
    /// <returns>An unsupported query feature exception with matching structured details.</returns>
    public static UnsupportedQueryFeatureException FromValidationFailure(QueryValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new(
            failure.Code,
            failure.Feature ?? "query",
            failure.Message,
            failure.Path);
    }
}
