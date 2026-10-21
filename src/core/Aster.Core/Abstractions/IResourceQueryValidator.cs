using Aster.Core.Models.Querying;

namespace Aster.Core.Abstractions;

/// <summary>
/// Validates resource queries against the active provider's declared capabilities.
/// </summary>
public interface IResourceQueryValidator
{
    /// <summary>
    /// Gets the active provider's capability declaration that <see cref="Validate"/> checks queries against, or
    /// <see langword="null"/> when the active provider declares none (every query then fails with
    /// <c>capabilities-not-declared</c>). Implementations that predate this member report none.
    /// </summary>
    QueryCapabilityDescription? Capabilities => null;

    /// <summary>
    /// Validates a query without mutating it.
    /// </summary>
    /// <param name="query">The resource query to validate.</param>
    /// <returns>A structured validation result.</returns>
    QueryValidationResult Validate(ResourceQuery query);
}
