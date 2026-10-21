using System.Data.Common;
using Aster.Persistence.Relational;
using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// The SQLite JSON1 dialect for the shared relational stores.
/// </summary>
internal sealed class SqliteJsonDialect(string connectionString) : RelationalJsonDialect
{
    // SQLITE_CONSTRAINT_PRIMARYKEY and SQLITE_CONSTRAINT_UNIQUE.
    private const int PrimaryKeyViolation = 1555;
    private const int UniqueViolation = 2067;

    public override int MaxParametersPerQuery => 500;

    public override async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // Microsoft.Data.Sqlite begins with BEGIN IMMEDIATE, which takes the database write lock up front: a
    // concurrent registration waits on the busy timeout instead of interleaving.
    public override async ValueTask<DbTransaction> BeginSerializedWriteAsync(
        DbConnection connection,
        string lockKey,
        CancellationToken cancellationToken) =>
        await connection.BeginTransactionAsync(cancellationToken);

    public override object TimestampValue(DateTime created) => created.ToUniversalTime().ToString("O");

    public override string ActiveVersionsContain(string activationAlias, string versionParameter) =>
        $"EXISTS (SELECT 1 FROM json_each({activationAlias}.payload, '$.activeVersions') WHERE value = {versionParameter})";

    public override string MarkerState => "json_extract(payload, '$.state')";

    public override bool IsUniqueViolation(DbException exception) =>
        exception is SqliteException { SqliteExtendedErrorCode: PrimaryKeyViolation or UniqueViolation };
}
