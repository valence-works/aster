using System.Diagnostics;
using System.Transactions;
using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Persistence.SqlServerJson;
using Aster.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqlServerJson;

/// <summary>
/// #152: the provider's infrastructure steps, applied from empty or part-way, by concurrent startups, or not at all.
/// </summary>
public sealed class SqlServerJsonInfrastructureRunnerTests : IDisposable
{
    // Not idempotent on purpose: a second run would fail on the table or add a second row.
    private static readonly SqlServerJsonInfrastructureStep CountingStep = new(
        "0100-counting",
        "Records each time it runs.",
        ["CREATE TABLE step_runs (run int); INSERT INTO step_runs VALUES (1);"]);

    private static readonly string[] SchemaStepIds = [.. SqlServerJsonSchema.Steps.Select(static step => step.Id)];

    private readonly SqlServerTestDatabase database = new();

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
        await new SqlServerJsonInfrastructureRunner(database.ConnectionString, SqlServerJsonSchema.Steps).ApplyAsync();
        var runner = new SqlServerJsonInfrastructureRunner(database.ConnectionString, [.. SqlServerJsonSchema.Steps, CountingStep]);

        Assert.Equal([CountingStep.Id], (await runner.GetPendingAsync()).Select(static step => step.Id));

        var applied = await runner.ApplyAsync();

        Assert.Equal([CountingStep.Id], applied.Select(static step => step.Id));
        Assert.Equal([.. SchemaStepIds, CountingStep.Id], (await runner.GetAppliedAsync()).Select(static step => step.Id));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM step_runs;"));
    }

    [SkippableFact]
    public async Task ApplyAsync_OnASchemaCreatedBeforeTheRunner_RecordsTheInitialStep()
    {
        foreach (var batch in SqlServerJsonSchema.Steps[0].Batches)
            Execute(batch);

        await using var provider = CreatePassiveProvider();
        var runner = provider.GetRequiredService<IInfrastructureRunner>();

        var applied = await runner.ApplyAsync();

        Assert.Equal(SchemaStepIds, applied.Select(static step => step.Id));
        Assert.True((await runner.VerifyAsync()).IsCurrent);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyAsync_TenConcurrentStartups_ApplyEachStepExactlyOnce(bool readCommittedSnapshot)
    {
        // Row versioning must not change the outcome: the runner reads the steps after taking its lock, and a read
        // committed snapshot is taken per statement.
        if (readCommittedSnapshot)
            Execute($"ALTER DATABASE [{new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;");

        // Hold the runner's lock until every startup is queued behind it, so they all wait on one another.
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var holding = (SqlTransaction)await holder.BeginTransactionAsync();
        await using (var take = new SqlCommand(
            $"EXEC sp_getapplock @Resource = N'{SqlServerJsonInfrastructureRunner.LockResource}', @LockMode = 'Exclusive', @LockOwner = 'Transaction';",
            holder,
            holding))
        {
            await take.ExecuteNonQueryAsync();
        }

        const int Startups = 10;
        var running = Enumerable.Range(0, Startups).Select(_ => Task.Run(async () =>
            await new SqlServerJsonInfrastructureRunner(database.ConnectionString, [.. SqlServerJsonSchema.Steps, CountingStep]).ApplyAsync())).ToList();
        while (Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT' AND resource_database_id = DB_ID();")) < Startups)
            await Task.Delay(20);

        await holding.RollbackAsync();
        var applied = await Task.WhenAll(running);

        // One startup applied every step; the other nine found them recorded.
        Assert.Single(applied, static steps => steps.Count == SchemaStepIds.Length + 1);
        Assert.Equal(Startups - 1, applied.Count(static steps => steps.Count == 0));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM step_runs;"));
        Assert.Equal(SchemaStepIds.Length + 1, Scalar("SELECT COUNT(*) FROM aster_infrastructure_steps;"));
    }

    [SkippableFact]
    public async Task FirstRequests_InABurst_DoNotWaitOnThePool()
    {
        // #202: a cold provider hit by many first requests at once. Startup that blocked them would need a free pool
        // thread to finish, so it would wait for the pool to grow past every blocked request.
        // More requests than connections: waiting for one is asynchronous, so that is no reason to be slow.
        const int Requests = 128;
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqlServerJson(options => options.ConnectionString = $"{database.ConnectionString};Max Pool Size=20")
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

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"Startup took {elapsed.Elapsed}.");
        Assert.True((await provider.GetRequiredService<IInfrastructureRunner>().VerifyAsync()).IsCurrent);
    }

    [SkippableFact]
    public async Task FirstRequest_InsideATransactionScope_AppliesTheSchemaOutsideIt()
    {
        // The first request's ambient transaction must not reach the shared run: its connection would enlist, and
        // the run's own transaction would then fail for every request waiting on it.
        await using var provider = database.CreateProvider();
        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await provider.GetRequiredService<IResourceDefinitionStore>().ListDefinitionsAsync();

        Assert.True((await provider.GetRequiredService<IInfrastructureRunner>().VerifyAsync()).IsCurrent);
    }

    [SkippableFact]
    public async Task FirstRequest_WithExecutionContextFlowSuppressed_AppliesTheSchema()
    {
        await using var provider = database.CreateProvider();
        Task first;
        using (ExecutionContext.SuppressFlow())
            first = provider.GetRequiredService<IResourceDefinitionStore>().ListDefinitionsAsync().AsTask();
        await first;

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

    [SkippableFact]
    public async Task ApplyAsync_BelowCompatibilityLevel130_FailsWithoutChangingTheSchema()
    {
        // OPENJSON needs compatibility level 130; the runner says so before the first query fails obscurely.
        Execute($"ALTER DATABASE [{new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog}] SET COMPATIBILITY_LEVEL = 120;");
        await using var provider = CreatePassiveProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetRequiredService<IInfrastructureRunner>().ApplyAsync());

        Assert.Contains("compatibility level 130", exception.Message);
        Assert.Empty(ReadTableNames());
    }

    private ServiceProvider CreatePassiveProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqlServerJson(options =>
            {
                options.ConnectionString = database.ConnectionString;
                options.InitializeSchema = false;
            })
            .BuildServiceProvider();

    private List<string> ReadTableNames()
    {
        using var connection = new SqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new SqlCommand("SELECT name FROM sys.tables;", connection);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names.Order(StringComparer.Ordinal).ToList();
    }

    private object? Scalar(string sql)
    {
        using var connection = new SqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return command.ExecuteScalar();
    }

    private void Execute(string sql) => Scalar(sql);
}
