using System.Collections.ObjectModel;

namespace Aster.Core.Models.Querying;

/// <summary>
/// Stable failure codes used by index projection declaration and evaluation.
/// </summary>
public static class IndexProjectionFailureCodes
{
    /// <summary>Projection source value is absent.</summary>
    public const string MissingSource = "missing-source";

    /// <summary>Projection source value does not match the declared field type.</summary>
    public const string IncompatibleValueShape = "incompatible-value-shape";

    /// <summary>Projection declaration is invalid.</summary>
    public const string InvalidProjectionDeclaration = "invalid-projection-declaration";

    /// <summary>Projection field name is duplicated in the declaration set.</summary>
    public const string DuplicateProjectionField = "duplicate-projection-field";
}

/// <summary>
/// Structured index projection failure.
/// </summary>
/// <param name="FieldName">Projection field name when available.</param>
/// <param name="Code">Stable failure code.</param>
/// <param name="Message">Human-readable failure message.</param>
/// <param name="Source">Projection source description when available.</param>
public sealed record IndexProjectionFailure(
    string? FieldName,
    string Code,
    string Message,
    string? Source = null);

/// <summary>
/// Result of validating an index projection declaration set.
/// </summary>
/// <param name="Failures">Validation failures.</param>
public sealed record IndexProjectionValidationResult(IReadOnlyList<IndexProjectionFailure> Failures)
{
    /// <summary>
    /// Gets a successful validation result.
    /// </summary>
    public static IndexProjectionValidationResult Success { get; } = new([]);

    /// <summary>
    /// Gets a value indicating whether the declaration set has no failures.
    /// </summary>
    public bool IsValid => Failures.Count == 0;

    /// <summary>
    /// Creates a validation result from mutable failures.
    /// </summary>
    /// <param name="failures">Validation failures.</param>
    /// <returns>An immutable validation result.</returns>
    public static IndexProjectionValidationResult Create(IEnumerable<IndexProjectionFailure> failures)
    {
        var list = failures.ToList();
        return list.Count == 0
            ? Success
            : new(new ReadOnlyCollection<IndexProjectionFailure>(list));
    }
}
