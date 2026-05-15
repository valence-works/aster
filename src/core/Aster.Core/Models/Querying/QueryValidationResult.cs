namespace Aster.Core.Models.Querying;

/// <summary>
/// Represents the result of validating a <see cref="ResourceQuery"/> against provider capabilities.
/// </summary>
/// <param name="Failures">Validation failures found in the query.</param>
public sealed record QueryValidationResult(IReadOnlyList<QueryValidationFailure> Failures)
{
    /// <summary>
    /// Gets a value indicating whether the query is valid for the provider.
    /// </summary>
    public bool IsValid => Failures.Count == 0;

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static QueryValidationResult Success { get; } = new([]);
}
