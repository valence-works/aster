using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Core.Abstractions;

/// <summary>
/// Executes portable <see cref="ResourceQuery"/> ASTs against a resource store.
/// </summary>
public interface IResourceQueryService
{
    /// <summary>
    /// Queries the resource store using the provided <see cref="ResourceQuery"/> AST.
    /// Returns the latest version of each matching resource.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching resource versions.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown if the query contains a <see cref="Aster.Core.Models.Querying.ComparisonOperator.Range"/> comparator
    /// (not supported by the Phase 1 in-memory evaluator).
    /// </exception>
    ValueTask<IEnumerable<Resource>> QueryAsync(ResourceQuery query, CancellationToken cancellationToken = default);
}
