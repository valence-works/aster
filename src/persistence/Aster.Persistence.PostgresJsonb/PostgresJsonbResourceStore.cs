using Aster.Persistence.Relational;
using Npgsql;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// PostgreSQL JSONB-backed implementation of Aster's low-level definition and resource version contracts.
/// </summary>
internal sealed class PostgresJsonbResourceStore : RelationalJsonResourceStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="PostgresJsonbResourceStore"/>, applying pending infrastructure
    /// steps when <see cref="PostgresJsonbAsterOptions.InitializeSchema"/> is set.
    /// </summary>
    /// <param name="dataSource">The provider's data source.</param>
    /// <param name="infrastructure">The provider's infrastructure runner.</param>
    /// <param name="options">Provider options.</param>
    public PostgresJsonbResourceStore(PostgresJsonbDataSource dataSource, PostgresJsonbInfrastructureRunner infrastructure, PostgresJsonbAsterOptions options)
        : base(new PostgresJsonbDialect(dataSource.Value))
    {
        if (options.InitializeSchema)
            infrastructure.EnsureApplied();
    }
}
