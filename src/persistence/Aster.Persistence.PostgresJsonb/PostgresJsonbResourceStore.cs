using Aster.Persistence.Relational;
using Npgsql;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// PostgreSQL JSONB-backed implementation of Aster's low-level definition and resource version contracts.
/// </summary>
internal sealed class PostgresJsonbResourceStore : RelationalJsonResourceStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="PostgresJsonbResourceStore"/>, creating the schema when
    /// <see cref="PostgresJsonbAsterOptions.InitializeSchema"/> is set (idempotent).
    /// </summary>
    /// <param name="dataSource">The provider's data source.</param>
    /// <param name="options">Provider options.</param>
    public PostgresJsonbResourceStore(PostgresJsonbDataSource dataSource, PostgresJsonbAsterOptions options)
        : base(new PostgresJsonbDialect(dataSource.Value))
    {
        if (options.InitializeSchema)
            PostgresJsonbSchema.Initialize(dataSource.Value);
    }
}
