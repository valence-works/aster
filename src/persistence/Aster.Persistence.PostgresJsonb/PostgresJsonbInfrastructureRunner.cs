using System.Data;
using Aster.Core.Abstractions;
using Aster.Core.Models.Infrastructure;
using Npgsql;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// A provider infrastructure step: SQL applied once and recorded in <c>aster_infrastructure_steps</c>.
/// </summary>
internal sealed record PostgresJsonbInfrastructureStep(string Id, string Description, string Sql);

/// <summary>
/// Applies the provider's steps in one transaction under a transaction-scoped advisory lock. A process that starts
/// while another applies waits, then finds the steps recorded and applies nothing, so concurrent startups run
/// each step once and never race on the catalog (<c>CREATE TABLE IF NOT EXISTS</c> alone can fail with a unique
/// violation in <c>pg_type</c>). A failing step rolls back every step of that run.
/// </summary>
internal sealed class PostgresJsonbInfrastructureRunner : IInfrastructureRunner
{
    // An arbitrary constant shared by every Aster process, so concurrent runs against one database serialize.
    internal const long LockKey = 0x4153_5445_5200_0001; // "ASTER", 1

    private const string StepsTable = """
        CREATE TABLE IF NOT EXISTS aster_infrastructure_steps (
            step_id text COLLATE "C" PRIMARY KEY,
            description text NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    private readonly NpgsqlDataSource dataSource;
    private readonly IReadOnlyList<PostgresJsonbInfrastructureStep> steps;
    private readonly IReadOnlyList<PostgresProjectionIndex> indexes;
    private readonly object startupGate = new();
    private Task? startup;

    public PostgresJsonbInfrastructureRunner(PostgresJsonbDataSource dataSource, PostgresJsonbAsterOptions options)
        : this(dataSource.Value, PostgresJsonbSchema.Steps, PostgresProjectionIndexes.Plan(options.IndexProjections))
    {
    }

    internal PostgresJsonbInfrastructureRunner(
        NpgsqlDataSource dataSource,
        IReadOnlyList<PostgresJsonbInfrastructureStep> steps,
        IReadOnlyList<PostgresProjectionIndex>? indexes = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(steps);
        this.dataSource = dataSource;
        this.steps = steps;
        this.indexes = indexes ?? [];
    }

    /// <summary>
    /// Applies pending steps once per runner, for <see cref="PostgresJsonbAsterOptions.InitializeSchema"/>. The stores
    /// await it before their first statement rather than blocking in their constructors: a blocked thread can't help
    /// finish the run, and a burst of first requests would otherwise wait for the pool to grow past all of them. A
    /// failed run isn't kept: the next caller tries again. Cancelling a caller stops its wait, not the shared run.
    /// </summary>
    public ValueTask EnsureAppliedAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (startupGate)
        {
            // Checked here rather than by the waiters: every waiter may have stopped waiting before the run failed.
            if (startup is { IsFaulted: true } or { IsCanceled: true })
                startup = null;

            // Shared, so it takes nothing from the first caller: no synchronization context to post to, and no
            // execution context, which could carry an ambient TransactionScope the run's connection would enlist in.
            if (startup is null)
            {
                using (ExecutionContext.SuppressFlow())
                    startup = Task.Run(async () => await ApplyAsync(CancellationToken.None).ConfigureAwait(false));
            }

            task = startup;
        }

        return task.IsCompletedSuccessfully ? ValueTask.CompletedTask : new(task.WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InfrastructureStep>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        // Read committed, explicitly (Npgsql's default too): the read after the lock must see the steps an earlier holder
        // committed, which a repeatable-read snapshot taken at the lock statement would not.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key);", connection, transaction))
        {
            command.Parameters.AddWithValue("key", LockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(StepsTable, connection, transaction))
            await command.ExecuteNonQueryAsync(cancellationToken);

        // Read after taking the lock: under read committed this sees whatever a run that held it committed.
        var applied = (await ReadAppliedAsync(connection, transaction, cancellationToken))
            .Select(static step => step.Id)
            .ToHashSet(StringComparer.Ordinal);
        var appliedNow = new List<InfrastructureStep>();
        foreach (var step in steps.Where(step => !applied.Contains(step.Id)))
        {
            await using (var command = new NpgsqlCommand(step.Sql, connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken);

            await using (var command = new NpgsqlCommand(
                "INSERT INTO aster_infrastructure_steps (step_id, description) VALUES (@id, @description);",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("id", step.Id);
                command.Parameters.AddWithValue("description", step.Description);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            appliedNow.Add(new(step.Id, step.Description));
        }

        // Projection indexes aren't recorded: the catalog is their record, so creating the declared ones and dropping
        // the rest converges on the declaration whatever happened before.
        var existing = await ReadProjectionIndexNamesAsync(connection, transaction, cancellationToken);
        foreach (var index in indexes.Where(index => !existing.Contains(index.Name)))
        {
            await using (var command = new NpgsqlCommand(index.Sql, connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken);

            appliedNow.Add(index.CreateStep);
        }

        foreach (var name in Undeclared(existing))
        {
            await using (var command = new NpgsqlCommand($"DROP INDEX IF EXISTS \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\";", connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken);

            appliedNow.Add(PostgresProjectionIndex.DropStep(name));
        }

        await transaction.CommitAsync(cancellationToken);
        return appliedNow;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InfrastructureStep>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var applied = (await ReadAppliedAsync(connection, transaction: null, cancellationToken)).Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        return [.. PendingSteps(applied), .. await PendingIndexStepsAsync(connection, cancellationToken)];
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AppliedInfrastructureStep>> GetAppliedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAppliedAsync(connection, transaction: null, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<InfrastructureVerification> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var applied = await ReadAppliedAsync(connection, transaction: null, cancellationToken);
        var known = steps.Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        var appliedIds = applied.Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        return new(
            [.. PendingSteps(appliedIds), .. await PendingIndexStepsAsync(connection, cancellationToken)],
            applied.Select(static step => step.Id).Where(id => !known.Contains(id)).ToList());
    }

    private IEnumerable<InfrastructureStep> PendingSteps(IReadOnlySet<string> applied) =>
        steps.Where(step => !applied.Contains(step.Id)).Select(static step => new InfrastructureStep(step.Id, step.Description));

    private async Task<IEnumerable<InfrastructureStep>> PendingIndexStepsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var existing = await ReadProjectionIndexNamesAsync(connection, transaction: null, cancellationToken);
        return indexes
            .Where(index => !existing.Contains(index.Name))
            .Select(static index => index.CreateStep)
            .Concat(Undeclared(existing).Select(PostgresProjectionIndex.DropStep));
    }

    private IEnumerable<string> Undeclared(IReadOnlySet<string> existing)
    {
        var declared = indexes.Select(static index => index.Name).ToHashSet(StringComparer.Ordinal);
        return existing.Where(name => !declared.Contains(name)).Order(StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> ReadProjectionIndexNamesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT indexname FROM pg_indexes
            WHERE schemaname = current_schema() AND tablename = 'resource_versions' AND starts_with(indexname, @prefix);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("prefix", PostgresProjectionIndexes.NamePrefix);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
            names.Add(reader.GetString(0));

        return names;
    }

    // Read-only: a database that was never initialized has no steps table and so no applied steps.
    private static async Task<List<AppliedInfrastructureStep>> ReadAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('aster_infrastructure_steps') IS NOT NULL;", connection, transaction))
        {
            if (await exists.ExecuteScalarAsync(cancellationToken) is not true)
                return [];
        }

        await using var command = new NpgsqlCommand(
            "SELECT step_id, applied_at FROM aster_infrastructure_steps ORDER BY applied_at, step_id;",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applied = new List<AppliedInfrastructureStep>();
        while (await reader.ReadAsync(cancellationToken))
            applied.Add(new(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));

        return applied;
    }
}
