using System.Data.Common;
using Aster.Persistence.Relational;
using Npgsql;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// The PostgreSQL <c>jsonb</c> dialect for the shared relational stores.
/// </summary>
/// <param name="dataSource">The provider's data source.</param>
/// <param name="ensureSchema">Awaited before every connection opens, so the schema exists first; <see langword="null" /> when the host applies it.</param>
internal sealed class PostgresJsonbDialect(NpgsqlDataSource dataSource, Func<CancellationToken, ValueTask>? ensureSchema) : RelationalJsonDialect
{
    // A statement binds at most 65,535 parameters; batches stay far below that.
    public override int MaxParametersPerQuery => 1000;

    public override async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (ensureSchema is not null)
            await ensureSchema(cancellationToken);

        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    // Read committed lets two registrations read the same latest version; a transaction-scoped advisory lock
    // on the key makes the second wait until the first commits.
    public override async ValueTask<DbTransaction> BeginSerializedWriteAsync(
        DbConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@lockKey, 0));";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@lockKey";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return transaction;
    }

    public override string JsonParameter(string parameterName) => $"{parameterName}::jsonb";

    public override object TimestampValue(DateTime created) => created.ToUniversalTime();

    public override string ActiveVersionsContain(string activationAlias, string versionParameter) =>
        $"{activationAlias}.payload -> 'activeVersions' @> to_jsonb({versionParameter})";

    public override string MarkerState => "(payload ->> 'state')::integer";

    public override bool IsUniqueViolation(DbException exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
