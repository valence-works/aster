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
    private const long LockKey = 0x4153_5445_5200_0001; // "ASTER", 1

    private const string StepsTable = """
        CREATE TABLE IF NOT EXISTS aster_infrastructure_steps (
            step_id text COLLATE "C" PRIMARY KEY,
            description text NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    private readonly NpgsqlDataSource dataSource;
    private readonly IReadOnlyList<PostgresJsonbInfrastructureStep> steps;
    private readonly object startupGate = new();
    private Task? startup;

    public PostgresJsonbInfrastructureRunner(PostgresJsonbDataSource dataSource)
        : this(dataSource.Value, PostgresJsonbSchema.Steps)
    {
    }

    internal PostgresJsonbInfrastructureRunner(NpgsqlDataSource dataSource, IReadOnlyList<PostgresJsonbInfrastructureStep> steps)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(steps);
        this.dataSource = dataSource;
        this.steps = steps;
    }

    /// <summary>
    /// Applies pending steps once per runner, for <see cref="PostgresJsonbAsterOptions.InitializeSchema"/>. Store
    /// constructors call it, so it blocks. A failed run isn't cached: the next caller tries again.
    /// </summary>
    public void EnsureApplied()
    {
        Task task;
        lock (startupGate)
            task = startup ??= ApplyAsync().AsTask();

        try
        {
            task.GetAwaiter().GetResult();
        }
        catch
        {
            lock (startupGate)
            {
                if (startup == task)
                    startup = null;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InfrastructureStep>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

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

        await transaction.CommitAsync(cancellationToken);
        return appliedNow;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InfrastructureStep>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        var applied = (await GetAppliedAsync(cancellationToken)).Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        return steps.Where(step => !applied.Contains(step.Id)).Select(static step => new InfrastructureStep(step.Id, step.Description)).ToList();
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
        var applied = await GetAppliedAsync(cancellationToken);
        var known = steps.Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        var appliedIds = applied.Select(static step => step.Id).ToHashSet(StringComparer.Ordinal);
        return new(
            steps.Where(step => !appliedIds.Contains(step.Id)).Select(static step => new InfrastructureStep(step.Id, step.Description)).ToList(),
            applied.Select(static step => step.Id).Where(id => !known.Contains(id)).ToList());
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
