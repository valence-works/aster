using System.Data;
using Aster.Core.Abstractions;
using Aster.Core.Models.Infrastructure;
using Aster.Persistence.Relational;
using Microsoft.Data.SqlClient;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// A provider infrastructure step: batches of SQL applied once, in order, and recorded in
/// <c>aster_infrastructure_steps</c>. Statements that must start a batch (<c>CREATE FUNCTION</c>,
/// <c>CREATE TRIGGER</c>) get a batch of their own.
/// </summary>
internal sealed record SqlServerJsonInfrastructureStep(string Id, string Description, IReadOnlyList<string> Batches);

/// <summary>
/// Applies the provider's steps and declared projections in one transaction under an exclusive, transaction-owned
/// application lock (<c>sp_getapplock</c>). A process that starts while another applies waits, then finds the steps
/// recorded and applies nothing, so concurrent startups run each step once. DDL is transactional on SQL Server: a
/// failing step rolls back every step of that run.
/// </summary>
internal sealed class SqlServerJsonInfrastructureRunner : IInfrastructureRunner
{
    /// <summary>The application lock every Aster process takes to change the schema of one database.</summary>
    internal const string LockResource = "aster:infrastructure";

    // OPENJSON needs compatibility level 130 (SQL Server 2016) or later; below it the function doesn't exist.
    private const int MinimumCompatibilityLevel = 130;

    private const string StepsTable = """
        IF OBJECT_ID(N'aster_infrastructure_steps', N'U') IS NULL
        CREATE TABLE aster_infrastructure_steps (
            step_id nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT pk_aster_infrastructure_steps PRIMARY KEY,
            description nvarchar(max) NOT NULL,
            applied_at datetimeoffset(7) NOT NULL CONSTRAINT df_aster_infrastructure_steps_applied_at DEFAULT SYSUTCDATETIME()
        );
        """;

    private readonly string connectionString;
    private readonly IReadOnlyList<SqlServerJsonInfrastructureStep> steps;
    private readonly IReadOnlyList<SqlServerProjection> projections;
    private readonly RelationalInfrastructureStartup startup;

    public SqlServerJsonInfrastructureRunner(SqlServerJsonAsterOptions options)
        : this(options.ConnectionString, SqlServerJsonSchema.Steps, SqlServerProjections.Plan(options.IndexProjections))
    {
    }

    internal SqlServerJsonInfrastructureRunner(
        string connectionString,
        IReadOnlyList<SqlServerJsonInfrastructureStep> steps,
        IReadOnlyList<SqlServerProjection>? projections = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(steps);
        this.connectionString = connectionString;
        this.steps = steps;
        this.projections = projections ?? [];
        startup = new RelationalInfrastructureStartup(async cancellationToken => await ApplyAsync(cancellationToken));
    }

    /// <summary>
    /// Applies pending steps once per runner, for <see cref="SqlServerJsonAsterOptions.InitializeSchema"/>, in one run
    /// every caller awaits (<see cref="RelationalInfrastructureStartup"/>).
    /// </summary>
    public ValueTask EnsureAppliedAsync(CancellationToken cancellationToken = default) =>
        startup.EnsureAppliedAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InfrastructureStep>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureCompatibilityLevelAsync(connection, cancellationToken);

        // Read committed, explicitly: the read after the lock must see the steps an earlier holder committed.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // Without a command timeout: a startup may wait as long as another process takes to build an index.
        await SqlServerJsonDialect.AcquireApplicationLockAsync(connection, transaction, LockResource, cancellationToken, commandTimeout: 0);
        await ExecuteAsync(connection, transaction, StepsTable, cancellationToken);

        // Read after taking the lock: this sees whatever a run that held it committed.
        var applied = (await ReadAppliedAsync(connection, transaction, cancellationToken))
            .Select(static step => step.Id)
            .ToHashSet(StringComparer.Ordinal);
        var appliedNow = new List<InfrastructureStep>();
        foreach (var step in steps.Where(step => !applied.Contains(step.Id)))
        {
            foreach (var batch in step.Batches)
                await ExecuteAsync(connection, transaction, batch, cancellationToken);

            await using (var command = new SqlCommand(
                "INSERT INTO aster_infrastructure_steps (step_id, description) VALUES (@id, @description);",
                connection,
                transaction))
            {
                command.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = step.Id });
                command.Parameters.Add(new SqlParameter("@description", SqlDbType.NVarChar, -1) { Value = step.Description });
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            appliedNow.Add(new(step.Id, step.Description));
        }

        appliedNow.AddRange(await ApplyProjectionsAsync(connection, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return appliedNow;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InfrastructureStep>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var applied = (await ReadAppliedAsync(connection, transaction: null, cancellationToken)).Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        return [.. PendingSteps(applied), .. await PendingProjectionStepsAsync(connection, cancellationToken)];
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AppliedInfrastructureStep>> GetAppliedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadAppliedAsync(connection, transaction: null, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<InfrastructureVerification> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var applied = await ReadAppliedAsync(connection, transaction: null, cancellationToken);
        var known = steps.Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        var appliedIds = applied.Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        return new(
            [.. PendingSteps(appliedIds), .. await PendingProjectionStepsAsync(connection, cancellationToken)],
            applied.Select(static step => step.Id).Where(id => !known.Contains(id)).ToList());
    }

    // Projections aren't recorded as steps: the catalog is their record (projection indexes on resource_versions, and
    // the registered array projections), so creating the declared ones and removing the rest converges on the
    // declaration whatever happened before.
    private async Task<List<InfrastructureStep>> ApplyProjectionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existing = await ReadProjectionNamesAsync(connection, transaction, cancellationToken);
        var created = projections.Where(projection => !existing.Names.Contains(projection.Name)).ToList();
        var dropped = Undeclared(existing.Names).ToList();
        var applied = new List<InfrastructureStep>();

        // The element trigger first: altering it locks resource_versions against writes until commit, so no version can
        // be inserted between the backfill below and the trigger that covers it.
        if (created.Any(static projection => projection.Kind == SqlServerProjectionKind.ArrayElements) || dropped.Any(existing.ArrayNames.Contains))
        {
            await ExecuteAsync(
                connection,
                transaction,
                SqlServerProjections.ElementsTriggerSql(projections) ?? $"DROP TRIGGER IF EXISTS {SqlServerProjections.ElementsTrigger};",
                cancellationToken);
        }

        foreach (var name in dropped)
        {
            var id = name[SqlServerProjections.NamePrefix.Length..];
            if (existing.ArrayNames.Contains(name))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    $"""
                    DELETE FROM aster_projection_elements WHERE projection_id = {SqlServerProjections.Literal(id)};
                    DELETE FROM aster_array_projections WHERE projection_id = {SqlServerProjections.Literal(id)};
                    """,
                    cancellationToken);
            }
            else
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    $"""
                    DROP INDEX IF EXISTS {name} ON resource_versions;
                    IF COL_LENGTH(N'resource_versions', N'{SqlServerProjections.ColumnPrefix}{id}') IS NOT NULL
                        ALTER TABLE resource_versions DROP COLUMN {SqlServerProjections.ColumnPrefix}{id};
                    """,
                    cancellationToken);
            }

            applied.Add(SqlServerProjection.DropStep(name));
        }

        foreach (var projection in created)
        {
            // A computed column left behind by an index that was dropped by hand is reused.
            foreach (var batch in projection.CreateBatches)
            {
                if (projection.Column is { } column && batch.StartsWith("ALTER TABLE", StringComparison.Ordinal) && existing.Columns.Contains(column))
                    continue;

                await ExecuteAsync(connection, transaction, batch, cancellationToken);
            }

            applied.Add(projection.CreateStep);
        }

        return applied;
    }

    private IEnumerable<InfrastructureStep> PendingSteps(IReadOnlySet<string> applied) =>
        steps.Where(step => !applied.Contains(step.Id)).Select(static step => new InfrastructureStep(step.Id, step.Description));

    private async Task<IEnumerable<InfrastructureStep>> PendingProjectionStepsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var existing = await ReadProjectionNamesAsync(connection, transaction: null, cancellationToken);
        return projections
            .Where(projection => !existing.Names.Contains(projection.Name))
            .Select(static projection => projection.CreateStep)
            .Concat(Undeclared(existing.Names).Select(SqlServerProjection.DropStep));
    }

    private IEnumerable<string> Undeclared(IReadOnlySet<string> existing)
    {
        var declared = projections.Select(static projection => projection.Name).ToHashSet(StringComparer.Ordinal);
        return existing.Where(name => !declared.Contains(name)).Order(StringComparer.Ordinal);
    }

    private sealed record ProjectionCatalog(HashSet<string> Names, HashSet<string> ArrayNames, HashSet<string> Columns);

    // Read-only, and empty on a database that was never initialized.
    private static async Task<ProjectionCatalog> ReadProjectionNamesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            $"""
            SELECT name, 0 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'resource_versions') AND name LIKE N'{SqlServerProjections.NamePrefix.Replace("_", "[_]", StringComparison.Ordinal)}%'
            UNION ALL
            SELECT name, 2 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'resource_versions') AND name LIKE N'{SqlServerProjections.ColumnPrefix.Replace("_", "[_]", StringComparison.Ordinal)}%';
            IF OBJECT_ID(N'aster_array_projections', N'U') IS NOT NULL
                EXEC (N'SELECT N''{SqlServerProjections.NamePrefix}'' + projection_id, 1 FROM aster_array_projections;');
            """,
            connection,
            transaction);
        var catalog = new ProjectionCatalog([], [], []);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        do
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                switch (reader.GetInt32(1))
                {
                    case 2:
                        catalog.Columns.Add(name);
                        break;
                    case 1:
                        catalog.ArrayNames.Add(name);
                        catalog.Names.Add(name);
                        break;
                    default:
                        catalog.Names.Add(name);
                        break;
                }
            }
        }
        while (await reader.NextResultAsync(cancellationToken));

        return catalog;
    }

    // Read-only: a database that was never initialized has no steps table and so no applied steps.
    private static async Task<List<AppliedInfrastructureStep>> ReadAppliedAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using (var exists = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'aster_infrastructure_steps', N'U') IS NULL THEN 0 ELSE 1 END;", connection, transaction))
        {
            if (await exists.ExecuteScalarAsync(cancellationToken) is not 1)
                return [];
        }

        await using var command = new SqlCommand(
            "SELECT step_id, applied_at FROM aster_infrastructure_steps ORDER BY applied_at, step_id;",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applied = new List<AppliedInfrastructureStep>();
        while (await reader.ReadAsync(cancellationToken))
            applied.Add(new(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));

        return applied;
    }

    private static async Task EnsureCompatibilityLevelAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT compatibility_level FROM sys.databases WHERE database_id = DB_ID();", connection);
        if (await command.ExecuteScalarAsync(cancellationToken) is byte level && level < MinimumCompatibilityLevel)
        {
            throw new InvalidOperationException(
                $"The Aster SQL Server JSON provider needs database compatibility level {MinimumCompatibilityLevel} or higher for OPENJSON; database '{connection.Database}' is at {level}. Raise it with ALTER DATABASE ... SET COMPATIBILITY_LEVEL.");
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
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

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        // Without a timeout: building an index or a computed column over a large table takes as long as it takes.
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
