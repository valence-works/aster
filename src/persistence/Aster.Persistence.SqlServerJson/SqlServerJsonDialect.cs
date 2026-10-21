using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Aster.Persistence.Relational;
using Aster.Persistence.SqlServerJson.Querying;
using Microsoft.Data.SqlClient;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// The SQL Server dialect for the shared relational stores: payloads in <c>nvarchar(max)</c>, read with
/// <c>OPENJSON</c>.
/// </summary>
/// <param name="connectionString">The provider's connection string.</param>
/// <param name="ensureSchema">Awaited before every connection opens, so the schema exists first; <see langword="null" /> when the host applies it.</param>
internal sealed class SqlServerJsonDialect(string connectionString, Func<CancellationToken, ValueTask>? ensureSchema) : RelationalJsonDialect
{
    // Violation of a PRIMARY KEY or UNIQUE constraint, and a duplicate key in a unique index.
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    // A statement binds at most 2,100 parameters; batches of composite keys bind twice this plus the tenant.
    public override int MaxParametersPerQuery => 1000;

    public override async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (ensureSchema is not null)
            await ensureSchema(cancellationToken);

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // Read committed lets two registrations read the same latest version; a transaction-owned application lock on the
    // key makes the second wait until the first commits.
    public override async ValueTask<DbTransaction> BeginSerializedWriteAsync(
        DbConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireApplicationLockAsync((SqlConnection)connection, transaction, ApplicationLockResource(lockKey), cancellationToken);
        return transaction;
    }

    public override string UpsertPayload(
        string tableName,
        IReadOnlyList<(string Column, string Parameter)> keys,
        string payloadParameter)
    {
        // HOLDLOCK keeps the match and the insert atomic, so two upserts of one key can't both insert.
        var source = string.Join(", ", keys.Select(static key => $"{key.Parameter} AS {key.Column}"));
        var match = string.Join(" AND ", keys.Select(static key => $"target.{key.Column} = source.{key.Column}"));
        var columns = string.Join(", ", keys.Select(static key => key.Column));
        return $"""
            MERGE {tableName} WITH (HOLDLOCK) AS target
            USING (SELECT {source}, {payloadParameter} AS payload) AS source
                ON {match}
            WHEN MATCHED THEN UPDATE SET payload = source.payload
            WHEN NOT MATCHED THEN INSERT ({columns}, payload)
                VALUES ({string.Join(", ", keys.Select(static key => $"source.{key.Column}"))}, source.payload);
            """;
    }

    // A table value constructor is a row source only in FROM.
    public override string KeyRows(IEnumerable<string> rows) =>
        $"SELECT first_key, second_key FROM (VALUES {string.Join(", ", rows)}) AS key_rows(first_key, second_key)";

    // A UTC DateTimeOffset binds as datetimeoffset, keeping all seven fraction digits; DateTime would bind as datetime.
    public override object TimestampValue(DateTime created) => new DateTimeOffset(created.ToUniversalTime());

    public override string ActiveVersionsContain(string activationAlias, string versionParameter) =>
        SqlServerQueryBuilder.ActiveVersionsContain(activationAlias, versionParameter);

    public override string MarkerState => "TRY_CONVERT(int, JSON_VALUE(payload, N'$.state'))";

    public override bool IsUniqueViolation(DbException exception) =>
        exception is SqlException { Number: UniqueConstraintViolation or UniqueIndexViolation };

    /// <summary>
    /// An application lock resource for <paramref name="key"/>: lock names are limited to 255 characters, so longer
    /// keys (tenant and resource IDs) are hashed.
    /// </summary>
    internal static string ApplicationLockResource(string key) =>
        key.Length <= 200 ? $"aster:{key}" : $"aster:sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}";

    /// <summary>
    /// Takes an exclusive application lock owned by <paramref name="transaction"/>. The lock waits indefinitely; the
    /// command's timeout (<paramref name="commandTimeout"/>, or the connection's default) bounds the wait.
    /// </summary>
    internal static async Task AcquireApplicationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string resource,
        CancellationToken cancellationToken,
        int? commandTimeout = null)
    {
        await using var command = new SqlCommand("sp_getapplock", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.NVarChar, 32) { Value = "Exclusive" });
        command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.NVarChar, 32) { Value = "Transaction" });
        command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = -1 });
        var result = new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
        command.Parameters.Add(result);
        if (commandTimeout is { } timeout)
            command.CommandTimeout = timeout;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // 0: granted at once; 1: granted after waiting. Negative values are timeouts, cancellations and deadlocks.
        if (result.Value is not int status || status < 0)
            throw new InvalidOperationException($"Could not take the SQL Server application lock '{resource}' (sp_getapplock returned {result.Value}).");
    }
}
