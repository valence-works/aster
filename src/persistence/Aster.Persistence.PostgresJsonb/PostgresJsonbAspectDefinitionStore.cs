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
    /// Initializes a new instance of <see cref="PostgresJsonbAspectDefinitionStore"/>, creating the schema when
    /// <see cref="PostgresJsonbAsterOptions.InitializeSchema"/> is set (idempotent).
    /// </summary>
    /// <param name="dataSource">The provider's data source.</param>
    /// <param name="options">Provider options.</param>
    public PostgresJsonbAspectDefinitionStore(PostgresJsonbDataSource dataSource, PostgresJsonbAsterOptions options)
        : base(new PostgresJsonbDialect(dataSource.Value))
    {
        if (options.InitializeSchema)
            PostgresJsonbSchema.Initialize(dataSource.Value);
    }
}
