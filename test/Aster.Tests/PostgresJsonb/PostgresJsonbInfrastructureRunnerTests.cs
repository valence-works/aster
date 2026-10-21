using System.Diagnostics;
using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Persistence.PostgresJsonb;
using Aster.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aster.Tests.PostgresJsonb;

/// <summary>
/// #99: the provider's infrastructure steps, applied from empty or part-way, by concurrent startups, or not at all.
/// </summary>
public sealed class PostgresJsonbInfrastructureRunnerTests : IDisposable
{
    // Not idempotent on purpose: a second run would fail on the table or add a second row.
    private static readonly PostgresJsonbInfrastructureStep CountingStep = new(
        "0100-counting",
        "Records each time it runs.",
        "CREATE TABLE step_runs (run integer); INSERT INTO step_runs VALUES (1);");

    private static readonly string[] SchemaStepIds = [.. PostgresJsonbSchema.Steps.Select(static step => step.Id)];

    private readonly PostgresTestDatabase database = new();

    public void Dispose() => database.Dispose();

    [SkippableFact]
    public async Task ApplyAsync_OnAnEmptyDatabase_AppliesEveryStepThenNothing()
    {
        await using var provider = CreatePassiveProvider();
        var runner = provider.GetRequiredService<IInfrastructureRunner>();

        var pending = await runner.GetPendingAsync();
        var applied = await runner.ApplyAsync();
        var again = await runner.ApplyAsync();
        var verification = await runner.VerifyAsync();

        Assert.Equal(SchemaStepIds, pending.Select(static step => step.Id));
        Assert.Equal(pending, applied);
        Assert.Empty(again);
        Assert.True(verification.IsCurrent);
        Assert.Equal(SchemaStepIds, (await runner.GetAppliedAsync()).Select(static step => step.Id));
        Assert.Contains("resource_versions", ReadTableNames());
    }

    [SkippableFact]
    public async Task ApplyAsync_OnADatabaseAtAnEarlierStep_AppliesOnlyTheRest()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await new PostgresJsonbInfrastructureRunner(dataSource, PostgresJsonbSchema.Steps).ApplyAsync();
        var runner = new PostgresJsonbInfrastructureRunner(dataSource, [.. PostgresJsonbSchema.Steps, CountingStep]);

        Assert.Equal([CountingStep.Id], (await runner.GetPendingAsync()).Select(static step => step.Id));

        var applied = await runner.ApplyAsync();

        Assert.Equal([CountingStep.Id], applied.Select(static step => step.Id));
        Assert.Equal([.. SchemaStepIds, CountingStep.Id], (await runner.GetAppliedAsync()).Select(static step => step.Id));
        Assert.Equal(1L, Scalar("SELECT count(*) FROM step_runs;"));
    }

    [SkippableFact]
    public async Task ApplyAsync_OnASchemaCreatedBeforeTheRunner_RecordsTheInitialStep()
    {
        Execute(PostgresJsonbSchema.Steps[0].Sql);
        await using var provider = CreatePassiveProvider();
        var runner = provider.GetRequiredService<IInfrastructureRunner>();

        var applied = await runner.ApplyAsync();

        Assert.Equal(SchemaStepIds, applied.Select(static step => step.Id));
        Assert.True((await runner.VerifyAsync()).IsCurrent);
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("repeatable read")]
    public async Task ApplyAsync_TenConcurrentStartups_ApplyEachStepExactlyOnce(string? defaultIsolation)
    {
        // A stricter server default must not change the outcome: the runner pins its own isolation level.
        if (defaultIsolation is not null)
            Execute($"ALTER DATABASE \"{new NpgsqlConnectionStringBuilder(database.ConnectionString).Database}\" SET default_transaction_isolation = '{defaultIsolation}';");

        var dataSources = Enumerable.Range(0, 10).Select(_ => NpgsqlDataSource.Create(database.ConnectionString)).ToList();
        try
        {
            // Hold the runner's lock until every startup is queued behind it, so they all wait on one another.
            await using var holder = new NpgsqlConnection(database.ConnectionString);
            await holder.OpenAsync();
            await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({PostgresJsonbInfrastructureRunner.LockKey});", holder))
                await take.ExecuteNonQueryAsync();

            var running = dataSources.Select(dataSource => Task.Run(async () =>
                await new PostgresJsonbInfrastructureRunner(dataSource, [.. PostgresJsonbSchema.Steps, CountingStep]).ApplyAsync())).ToList();
            while ((long)Scalar("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted;")! < dataSources.Count)
                await Task.Delay(20);

            await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({PostgresJsonbInfrastructureRunner.LockKey});", holder))
                await release.ExecuteNonQueryAsync();
            var applied = await Task.WhenAll(running);

            // One startup applied every step; the other nine found them recorded.
            Assert.Single(applied, static steps => steps.Count == SchemaStepIds.Length + 1);
            Assert.Equal(9, applied.Count(static steps => steps.Count == 0));
            Assert.Equal(1L, Scalar("SELECT count(*) FROM step_runs;"));
            Assert.Equal((long)SchemaStepIds.Length + 1, Scalar("SELECT count(*) FROM aster_infrastructure_steps;"));
        }
        finally
        {
            foreach (var dataSource in dataSources)
                await dataSource.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task FirstRequests_InABurst_DoNotWaitOnThePool()
    {
        // #202: a cold provider hit by many first requests at once. Startup that blocked them would need a free pool
        // thread to finish (even synchronous socket I/O completes on the pool on Linux), so it would wait for the
        // pool to grow past every blocked request, about two threads a second.
        // More requests than connections: waiting for one is asynchronous, so that is no reason to be slow.
        const int Requests = 128;
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterPostgresJsonb(options => options.ConnectionString = $"{database.ConnectionString};Maximum Pool Size=20")
            .BuildServiceProvider();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Enumerable.Range(0, Requests).Select(request => Task.Run(async () =>
        {
            await start.Task;
            await provider.GetRequiredService<IResourceManager>().GetLatestVersionAsync($"missing-{request}");
        })).ToList();

        var elapsed = Stopwatch.StartNew();
        start.SetResult();
        await Task.WhenAll(running);

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"Startup took {elapsed.Elapsed}.");
        Assert.True((await provider.GetRequiredService<IInfrastructureRunner>().VerifyAsync()).IsCurrent);
    }

    [SkippableFact]
    public async Task InitializeSchemaFalse_StaysPassive()
    {
        await using var provider = CreatePassiveProvider();
        _ = provider.GetRequiredService<IResourceDefinitionStore>();
        _ = provider.GetRequiredService<IAspectDefinitionStore>();
        _ = provider.GetRequiredService<IResourceQueryService>();
        var runner = provider.GetRequiredService<IInfrastructureRunner>();

        var pending = await runner.GetPendingAsync();
        var verification = await runner.VerifyAsync();

        Assert.Equal(SchemaStepIds, pending.Select(static step => step.Id));
        Assert.False(verification.IsCurrent);
        Assert.Empty(ReadTableNames());
    }

    [SkippableFact]
    public async Task VerifyAsync_ReportsStepsAppliedByANewerProvider()
    {
        await using var provider = database.CreateProvider();
        await provider.GetRequiredService<IResourceDefinitionStore>().ListDefinitionsAsync();
        Execute("INSERT INTO aster_infrastructure_steps (step_id, description) VALUES ('9999-from-the-future', 'Newer');");

        var verification = await provider.GetRequiredService<IInfrastructureRunner>().VerifyAsync();

        Assert.Empty(verification.PendingSteps);
        Assert.Equal(["9999-from-the-future"], verification.UnknownAppliedStepIds);
        Assert.False(verification.IsCurrent);
    }

    private ServiceProvider CreatePassiveProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterPostgresJsonb(options =>
            {
                options.ConnectionString = database.ConnectionString;
                options.InitializeSchema = false;
            })
            .BuildServiceProvider();

    private List<string> ReadTableNames()
    {
        using var connection = new NpgsqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;",
            connection);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names;
    }

    private object? Scalar(string sql)
    {
        using var connection = new NpgsqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return command.ExecuteScalar();
    }

    private void Execute(string sql) => Scalar(sql);
}
