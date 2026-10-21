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

        Assert.Equal(["0001-initial-schema"], pending.Select(static step => step.Id));
        Assert.Equal(pending, applied);
        Assert.Empty(again);
        Assert.True(verification.IsCurrent);
        Assert.Equal(["0001-initial-schema"], (await runner.GetAppliedAsync()).Select(static step => step.Id));
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
        Assert.Equal(["0001-initial-schema", CountingStep.Id], (await runner.GetAppliedAsync()).Select(static step => step.Id));
        Assert.Equal(1L, Scalar("SELECT count(*) FROM step_runs;"));
    }

    [SkippableFact]
    public async Task ApplyAsync_OnASchemaCreatedBeforeTheRunner_RecordsTheInitialStep()
    {
        Execute(PostgresJsonbSchema.Steps[0].Sql);
        await using var provider = CreatePassiveProvider();
        var runner = provider.GetRequiredService<IInfrastructureRunner>();

        var applied = await runner.ApplyAsync();

        Assert.Equal(["0001-initial-schema"], applied.Select(static step => step.Id));
        Assert.True((await runner.VerifyAsync()).IsCurrent);
    }

    [SkippableFact]
    public async Task ApplyAsync_TenConcurrentStartups_ApplyEachStepExactlyOnce()
    {
        var dataSources = Enumerable.Range(0, 10).Select(_ => NpgsqlDataSource.Create(database.ConnectionString)).ToList();
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = dataSources.Select(dataSource => Task.Run(async () =>
            {
                await start.Task;
                return await new PostgresJsonbInfrastructureRunner(dataSource, [.. PostgresJsonbSchema.Steps, CountingStep]).ApplyAsync();
            })).ToList();
            start.SetResult();
            var applied = await Task.WhenAll(running);

            // One startup applied both steps; the other nine found them recorded.
            Assert.Single(applied, static steps => steps.Count == 2);
            Assert.Equal(9, applied.Count(static steps => steps.Count == 0));
            Assert.Equal(1L, Scalar("SELECT count(*) FROM step_runs;"));
            Assert.Equal(2L, Scalar("SELECT count(*) FROM aster_infrastructure_steps;"));
        }
        finally
        {
            foreach (var dataSource in dataSources)
                await dataSource.DisposeAsync();
        }
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

        Assert.Single(pending);
        Assert.False(verification.IsCurrent);
        Assert.Empty(ReadTableNames());
    }

    [SkippableFact]
    public async Task VerifyAsync_ReportsStepsAppliedByANewerProvider()
    {
        await using var provider = database.CreateProvider();
        _ = provider.GetRequiredService<IResourceDefinitionStore>();
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
