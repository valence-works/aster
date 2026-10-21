using Aster.Persistence.Relational;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// SQL Server JSON-backed implementation of Aster's low-level definition and resource version contracts.
/// </summary>
internal sealed class SqlServerJsonResourceStore : RelationalJsonResourceStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="SqlServerJsonResourceStore"/>. When
    /// <see cref="SqlServerJsonAsterOptions.InitializeSchema"/> is set, pending infrastructure steps are applied
    /// before its first statement.
    /// </summary>
    /// <param name="infrastructure">The provider's infrastructure runner.</param>
    /// <param name="options">Provider options.</param>
    public SqlServerJsonResourceStore(SqlServerJsonInfrastructureRunner infrastructure, SqlServerJsonAsterOptions options)
        : base(new SqlServerJsonDialect(options.ConnectionString, options.InitializeSchema ? infrastructure.EnsureAppliedAsync : null))
    {
    }
}
