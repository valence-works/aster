using System.Diagnostics;
using System.Globalization;
using Elsa.Agents;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Agents.Persistence.EFCore;
using Elsa.Agents.Persistence.Filters;
using Elsa.Persistence.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// Store latency, Elsa's stores against the Aster store, for the results write-up (docs/consumers/elsa-agents-results.md).
/// It runs only when <c>ASTER_AGENTS_BENCHMARK=1</c> is set, in Release:
/// <c>ASTER_AGENTS_BENCHMARK=1 dotnet test test/Aster.Elsa.Agents.Tests -c Release -f net10.0 --filter AgentStoreLatencyBenchmark --logger "console;verbosity=detailed"</c>.
/// </summary>
/// <remarks>
/// Each pairing runs on the same storage tier: Elsa's in-memory store against Aster's in-memory provider, and Elsa's EF
/// Core store against Aster's SQLite JSON provider, each on its own SQLite file with connection pooling on. Both stores hold the same
/// <see cref="AgentCount"/> agents. Every operation is timed one call at a time with <see cref="Stopwatch"/> after a
/// warm-up, through Elsa's <see cref="IAgentStore"/> resolved from one DI scope, and reported as median and p95.
/// </remarks>
public sealed class AgentStoreLatencyBenchmark(ITestOutputHelper output)
{
    private const int AgentCount = 100;
    private const int Warmup = 50;
    private const int Iterations = 500;

    [SkippableFact]
    public async Task Measure()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("ASTER_AGENTS_BENCHMARK") == "1", "Set ASTER_AGENTS_BENCHMARK=1 to measure store latency.");

        output.WriteLine($"| Store | Get by ID | Find by name | List ({AgentCount}) | Update | Add |");
        output.WriteLine("|---|---|---|---|---|---|");

        await using (var elsaMemory = AgentTestHost.Start(useAsterStore: false))
            await MeasureAsync("Elsa in-memory (`MemoryAgentStore`)", elsaMemory);

        await using (var asterMemory = AgentTestHost.Start())
            await MeasureAsync("Aster store, in-memory provider", asterMemory);

        var database = AgentTestHost.NewDatabase();
        try
        {
            await using var elsaEf = AgentTestHost.Start(
                useAsterStore: false,
                persistence: feature => feature.UseEntityFrameworkCore(ef => ef.UseSqlite(AgentTestHost.ConnectionString(database, pooling: true))));
            await using (var context = await elsaEf.Services.GetRequiredService<IDbContextFactory<AgentsDbContext>>().CreateDbContextAsync())
                await context.Database.MigrateAsync();
            await MeasureAsync("Elsa EF Core, SQLite (`EFCoreAgentStore`)", elsaEf);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }

        await using (var asterSqlite = AgentTestHost.Start(AgentTestHost.Sqlite, pooling: true))
            await MeasureAsync("Aster store, SQLite JSON provider", asterSqlite);
    }

    private async Task MeasureAsync(string name, AgentTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentStore>();
        for (var index = 0; index < AgentCount; index++)
            await store.AddAsync(TestAgents.Create($"agent-{index:D3}", $"Agent {index:D3}"));

        var random = new Random(42);
        string Id() => $"agent-{random.Next(AgentCount):D3}";
        var updated = TestAgents.Create("agent-000", "Agent 000");
        var added = 0;

        var get = await TimeAsync(() => store.GetAsync(Id()));
        var find = await TimeAsync(() => store.FindAsync(new AgentDefinitionFilter { Name = $"Agent {random.Next(AgentCount):D3}" }));
        var list = await TimeAsync(() => store.ListAsync());
        var update = await TimeAsync(async () =>
        {
            updated.AgentConfig.PromptTemplate = $"Prompt {random.Next()}";
            await store.UpdateAsync(updated);
            return updated;
        });
        var add = await TimeAsync(async () =>
        {
            var agent = TestAgents.Create($"new-{added}", $"New {added++}");
            await store.AddAsync(agent);
            return agent;
        });

        output.WriteLine($"| {name} | {get} | {find} | {list} | {update} | {add} |");
    }

    private static async Task<string> TimeAsync<T>(Func<Task<T>> operation)
    {
        for (var index = 0; index < Warmup; index++)
            await operation();

        var samples = new double[Iterations];
        for (var index = 0; index < Iterations; index++)
        {
            var started = Stopwatch.GetTimestamp();
            await operation();
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        }

        Array.Sort(samples);
        return string.Create(CultureInfo.InvariantCulture, $"{Format(samples[Iterations / 2])} / {Format(samples[(int)(Iterations * 0.95)])}");
    }

    private static string Format(double microseconds) =>
        microseconds >= 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{microseconds / 1000:0.00} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{microseconds:0} µs");
}
