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
    private static readonly char[] Digits = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];

    private static readonly HashSet<string> IdentifierParameters = new(StringComparer.Ordinal)
    {
        "@tenantId", "@resourceId", "@definitionId", "@aspectDefinitionId", "@channel", "@keyFirst", "@keySecond",
    };

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
    /// A statement that inserts a row keyed by <paramref name="keys"/> with the JSON payload parameter
    /// <paramref name="payloadParameter"/>, or replaces the payload of the row that already has that key.
    /// </summary>
    /// <param name="tableName">The table.</param>
    /// <param name="keys">The key columns and the parameters bound to them, in primary-key order.</param>
    /// <param name="payloadParameter">The payload parameter, for example <c>@payload</c>.</param>
    public virtual string UpsertPayload(
        string tableName,
        IReadOnlyList<(string Column, string Parameter)> keys,
        string payloadParameter)
    {
        var columns = string.Join(", ", keys.Select(static key => key.Column));
        return $"""
            INSERT INTO {tableName} ({columns}, payload)
            VALUES ({string.Join(", ", keys.Select(static key => key.Parameter))}, {JsonParameter(payloadParameter)})
            ON CONFLICT({columns})
            DO UPDATE SET payload = excluded.payload;
            """;
    }

    /// <summary>
    /// The body of a common table expression with the columns <c>(first_key, second_key)</c> whose rows are
    /// <paramref name="rows"/>, each a parenthesized pair of parameters.
    /// </summary>
    public virtual string KeyRows(IEnumerable<string> rows) => $"VALUES {string.Join(", ", rows)}";

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
    /// Passes a value bound to an identifier parameter (<c>@tenantId</c>, <c>@resourceId</c>, <c>@definitionId</c>,
    /// <c>@aspectDefinitionId</c>, <c>@channel</c>, or a numbered composite-key parameter) to
    /// <see cref="ValidateIdentifier"/>. Other parameters are not checked.
    /// </summary>
    /// <param name="parameterName">The statement parameter.</param>
    /// <param name="value">The value bound to it.</param>
    public void ValidateParameter(string parameterName, object? value)
    {
        if (value is string identifier && IdentifierParameters.Contains(parameterName.TrimEnd(Digits)))
            ValidateIdentifier(identifier, parameterName);
    }

    /// <summary>
    /// Throws when <paramref name="identifier"/>, a tenant, definition, resource, aspect definition or channel ID
    /// bound to a statement, would match a different stored ID. The default accepts every value.
    /// </summary>
    /// <param name="identifier">The identifier value.</param>
    /// <param name="parameterName">The statement parameter it is bound to.</param>
    public virtual void ValidateIdentifier(string identifier, string parameterName)
    {
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is a primary-key or unique-constraint violation.
    /// </summary>
    public abstract bool IsUniqueViolation(DbException exception);
}
