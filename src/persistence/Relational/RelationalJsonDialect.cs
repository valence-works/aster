using System.Data.Common;

namespace Aster.Persistence.Relational;

/// <summary>
/// What a relational JSON provider's stores need from its database (ADR 0004). The stores share their SQL;
/// a dialect supplies connections, the few JSON expressions and column types that differ, write serialization
/// and error classification.
/// </summary>
/// <remarks>
/// The relational sources are linked into each provider assembly, so every provider compiles its own copy
/// and nothing here is public API.
/// </remarks>
internal abstract class RelationalJsonDialect
{
    /// <summary>
    /// The most parameters one statement may bind; bounded-ID reads split larger sets into batches.
    /// </summary>
    public abstract int MaxParametersPerQuery { get; }

    /// <summary>
    /// Opens a connection to the provider's database.
    /// </summary>
    public abstract ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Begins a transaction in which reading the latest version of <paramref name="lockKey"/> and inserting the
    /// next one cannot interleave with another registration of the same key.
    /// </summary>
    public abstract ValueTask<DbTransaction> BeginSerializedWriteAsync(
        DbConnection connection,
        string lockKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// The SQL for a bound JSON payload parameter, for example <c>@payload</c> or <c>@payload::jsonb</c>.
    /// </summary>
    public virtual string JsonParameter(string parameterName) => parameterName;

    /// <summary>
    /// The value bound to a <c>created</c> column for <paramref name="created"/>.
    /// </summary>
    public abstract object TimestampValue(DateTime created);

    /// <summary>
    /// A predicate over the activation row aliased <paramref name="activationAlias"/> that is true when its
    /// payload's <c>activeVersions</c> array contains the integer parameter <paramref name="versionParameter"/>.
    /// </summary>
    public abstract string ActiveVersionsContain(string activationAlias, string versionParameter);

    /// <summary>
    /// An integer expression for the <c>state</c> member of a lifecycle marker's <c>payload</c> column.
    /// </summary>
    public abstract string MarkerState { get; }

    /// <summary>
    /// Whether <paramref name="exception"/> is a primary-key or unique-constraint violation.
    /// </summary>
    public abstract bool IsUniqueViolation(DbException exception);
}
