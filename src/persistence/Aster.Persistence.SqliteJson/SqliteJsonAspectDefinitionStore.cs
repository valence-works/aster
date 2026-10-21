using Aster.Core.Abstractions;
using Aster.Persistence.Relational;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// SQLite JSON implementation of <see cref="IAspectDefinitionStore"/>, over the additive
/// <c>aspect_definitions</c> table (ADR 0003).
/// </summary>
internal sealed class SqliteJsonAspectDefinitionStore : RelationalJsonAspectDefinitionStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="SqliteJsonAspectDefinitionStore"/>, creating the schema when
    /// <see cref="SqliteJsonAsterOptions.InitializeSchema"/> is set (idempotent).
    /// </summary>
    /// <param name="options">The provider options.</param>
    public SqliteJsonAspectDefinitionStore(SqliteJsonAsterOptions options)
        : base(SqliteJsonResourceStore.CreateDialect(options))
    {
        if (options.InitializeSchema)
            SqliteJsonSchema.Initialize(options.ConnectionString);
    }
}
