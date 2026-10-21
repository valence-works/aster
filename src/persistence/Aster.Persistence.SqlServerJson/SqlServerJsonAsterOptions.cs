using Aster.Core.Models.Querying;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// Options for the SQL Server JSON provider.
/// </summary>
public sealed class SqlServerJsonAsterOptions
{
    /// <summary>
    /// Gets or sets the <c>Microsoft.Data.SqlClient</c> connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the provider applies its pending infrastructure steps (tables, functions and projection
    /// indexes) on first use. Safe when several processes start at once: each step runs once. Turn it off to apply
    /// them explicitly, from a deployment step through <c>IInfrastructureRunner</c>; the provider then never changes
    /// the schema itself.
    /// </summary>
    public bool InitializeSchema { get; set; } = true;

    /// <summary>
    /// Gets the index projections to back with physical indexes. The infrastructure runner creates the computed
    /// columns, indexes and element rows of each declared projection and removes those of projections no longer
    /// declared; queries over a projected facet or metadata field can then use them. Declaring a projection never
    /// changes query results.
    /// </summary>
    public IList<IndexProjection> IndexProjections { get; } = new List<IndexProjection>();
}
