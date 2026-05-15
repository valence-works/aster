using Aster.Core.Models.Querying;

namespace Aster.Core.Abstractions;

/// <summary>
/// Validates resource queries against the active provider's declared capabilities.
/// </summary>
public interface IResourceQueryValidator
{
    /// <summary>
    /// Validates a query without mutating it.
    /// </summary>
    /// <param name="query">The resource query to validate.</param>
    /// <returns>A structured validation result.</returns>
    QueryValidationResult Validate(ResourceQuery query);
}
