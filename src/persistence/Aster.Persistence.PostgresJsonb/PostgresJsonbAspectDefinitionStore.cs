using Aster.Core.Abstractions;
using Aster.Persistence.Relational;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// PostgreSQL JSONB implementation of <see cref="IAspectDefinitionStore"/>, over the <c>aspect_definitions</c>
/// table (ADR 0003).
/// </summary>
internal sealed class PostgresJsonbAspectDefinitionStore : RelationalJsonAspectDefinitionStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="PostgresJsonbAspectDefinitionStore"/>. When
    /// <see cref="PostgresJsonbAsterOptions.InitializeSchema"/> is set, pending infrastructure steps are applied
    /// before its first statement.
    /// </summary>
    /// <param name="dataSource">The provider's data source.</param>
    /// <param name="infrastructure">The provider's infrastructure runner.</param>
    /// <param name="options">Provider options.</param>
    public PostgresJsonbAspectDefinitionStore(PostgresJsonbDataSource dataSource, PostgresJsonbInfrastructureRunner infrastructure, PostgresJsonbAsterOptions options)
        : base(new PostgresJsonbDialect(dataSource.Value, options.InitializeSchema ? infrastructure.EnsureAppliedAsync : null))
    {
    }
}
