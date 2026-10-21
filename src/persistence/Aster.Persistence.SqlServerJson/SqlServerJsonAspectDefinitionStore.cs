using Aster.Core.Abstractions;
using Aster.Persistence.Relational;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// SQL Server JSON implementation of <see cref="IAspectDefinitionStore"/>, over the <c>aspect_definitions</c>
/// table (ADR 0003).
/// </summary>
internal sealed class SqlServerJsonAspectDefinitionStore : RelationalJsonAspectDefinitionStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="SqlServerJsonAspectDefinitionStore"/>. When
    /// <see cref="SqlServerJsonAsterOptions.InitializeSchema"/> is set, pending infrastructure steps are applied
    /// before its first statement.
    /// </summary>
    /// <param name="infrastructure">The provider's infrastructure runner.</param>
    /// <param name="options">Provider options.</param>
    public SqlServerJsonAspectDefinitionStore(SqlServerJsonInfrastructureRunner infrastructure, SqlServerJsonAsterOptions options)
        : base(new SqlServerJsonDialect(options.ConnectionString, options.InitializeSchema ? infrastructure.EnsureAppliedAsync : null))
    {
    }
}
