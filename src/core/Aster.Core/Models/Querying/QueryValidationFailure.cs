namespace Aster.Core.Models.Querying;

/// <summary>
/// Represents a single unsupported or invalid query feature found during validation.
/// </summary>
/// <param name="Code">Stable failure code suitable for tests and documentation.</param>
/// <param name="Message">Human-readable actionable explanation.</param>
/// <param name="Path">Optional location within the query shape.</param>
/// <param name="Feature">Optional unsupported feature category.</param>
public sealed record QueryValidationFailure(
    string Code,
    string Message,
    string? Path = null,
    string? Feature = null);
