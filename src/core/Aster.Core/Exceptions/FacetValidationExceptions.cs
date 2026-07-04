using Aster.Core.Models.Definitions;

namespace Aster.Core.Exceptions;

/// <summary>
/// Thrown by definition registration when facet metadata fails self-validation.
/// When thrown, registration was rejected before any state change; the store is unchanged.
/// </summary>
public sealed class FacetMetadataValidationException : Exception
{
    /// <summary>
    /// The full set of self-validation failures, ordered by aspect key, then facet
    /// definition id, then code (ordinal).
    /// </summary>
    public IReadOnlyList<FacetMetadataDiagnostic> Diagnostics { get; }

    /// <inheritdoc />
    public FacetMetadataValidationException()
        : base("Facet metadata failed self-validation.")
    {
        Diagnostics = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FacetMetadataValidationException"/> class.
    /// </summary>
    /// <param name="diagnostics">The ordered self-validation failures.</param>
    public FacetMetadataValidationException(IReadOnlyList<FacetMetadataDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public FacetMetadataValidationException(string message)
        : base(message)
    {
        Diagnostics = [];
    }

    /// <inheritdoc />
    public FacetMetadataValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Diagnostics = [];
    }

    private static string BuildMessage(IReadOnlyList<FacetMetadataDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return $"Facet metadata failed self-validation with {diagnostics.Count} diagnostic(s): "
            + string.Join("; ", diagnostics.Select(static diagnostic =>
                $"[{diagnostic.Code}] {diagnostic.AspectKey}/{diagnostic.FacetDefinitionId}: {diagnostic.Message}"));
    }
}

/// <summary>
/// Thrown by resource save (create/update) when present facet values violate declared
/// metadata constraints. When thrown, nothing was persisted and no before-save lifecycle
/// hook ran for that save.
/// </summary>
public sealed class FacetValueValidationException : Exception
{
    /// <summary>
    /// The full set of value violations, ordered by aspect key, then facet definition id,
    /// then code (ordinal).
    /// </summary>
    public IReadOnlyList<FacetValueDiagnostic> Diagnostics { get; }

    /// <inheritdoc />
    public FacetValueValidationException()
        : base("Facet values failed validation against declared metadata constraints.")
    {
        Diagnostics = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FacetValueValidationException"/> class.
    /// </summary>
    /// <param name="diagnostics">The ordered value violations.</param>
    public FacetValueValidationException(IReadOnlyList<FacetValueDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public FacetValueValidationException(string message)
        : base(message)
    {
        Diagnostics = [];
    }

    /// <inheritdoc />
    public FacetValueValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Diagnostics = [];
    }

    private static string BuildMessage(IReadOnlyList<FacetValueDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return $"Facet values failed validation with {diagnostics.Count} diagnostic(s): "
            + string.Join("; ", diagnostics.Select(static diagnostic =>
                $"[{diagnostic.Code}] {diagnostic.AspectKey}/{diagnostic.FacetDefinitionId}: {diagnostic.Message}"));
    }
}
