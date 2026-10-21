using System.Text.Json;
using Aster.Core.Extensions;
using Aster.Persistence.PostgresJsonb;
using Aster.Persistence.SqlServerJson;
using Aster.Persistence.SqliteJson;
using Elsa.Agents;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Agents.Persistence.Entities;
using Elsa.Agents.Persistence.Features;
using Elsa.Common.Multitenancy;
using Elsa.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// An in-process Elsa host with Elsa's agents feature, on an Aster provider. With <c>useAsterStore</c> Elsa's agent
/// persistence feature uses the Aster agent store (<c>UseAsterAgentStore</c>); without it Elsa keeps its own store.
/// </summary>
internal sealed class AgentTestHost : IAsyncDisposable
{
    public const string InMemory = "in-memory";
    public const string Sqlite = "sqlite";
    public const string Postgres = "postgres";
    public const string SqlServer = "sqlserver";

    private readonly ServiceProvider services;
    private readonly string? database;
    private readonly bool ownsDatabase;
    private readonly ServerDatabase? serverDatabase;

    private AgentTestHost(ServiceProvider services, string? database, bool ownsDatabase, ServerDatabase? serverDatabase)
    {
        this.services = services;
        this.database = database;
        this.ownsDatabase = ownsDatabase;
        this.serverDatabase = serverDatabase;
    }

    /// <summary>The providers the store contract runs on: Postgres and SQL Server when their servers are configured.</summary>
    public static TheoryData<string> AllProviders => new() { InMemory, Sqlite, Postgres, SqlServer };

    public IServiceProvider Services => services;

    /// <summary>The SQLite database file, when the host runs on SQLite.</summary>
    public string? Database => database;

    public static AgentTestHost Start(
        string provider = InMemory,
        Action<AsterAgentStoreOptions>? configure = null,
        bool useAsterStore = true,
        Action<AgentPersistenceFeature>? persistence = null,
        Action<AsterOptions>? aster = null,
        string? database = null,
        bool pooling = false)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddAsterCore(aster ?? (static _ => { }));

        var ownsDatabase = false;
        if (provider == Sqlite)
        {
            ownsDatabase = database is null;
            database ??= NewDatabase();
            collection.AddAsterSqliteJson(options => options.ConnectionString = ConnectionString(database, pooling));
        }

        ServerDatabase? serverDatabase = null;
        if (provider is Postgres or SqlServer)
        {
            serverDatabase = new ServerDatabase(provider);
            if (provider == Postgres)
                collection.AddAsterPostgresJsonb(options => options.ConnectionString = serverDatabase.ConnectionString);
            else
                collection.AddAsterSqlServerJson(options => options.ConnectionString = serverDatabase.ConnectionString);
        }

        collection.AddElsa(elsa => elsa
            .UseAgents()
            .UseAgentPersistence(feature =>
            {
                persistence?.Invoke(feature);
                if (useAsterStore)
                    feature.UseAsterAgentStore(configure);
            }));

        return new AgentTestHost(collection.BuildServiceProvider(), database, ownsDatabase, serverDatabase);
    }

    public static string NewDatabase() => Path.Combine(Path.GetTempPath(), $"aster-elsa-agents-{Guid.NewGuid():N}.db");

    public static string ConnectionString(string database, bool pooling = false) => $"Data Source={database};Pooling={pooling}";

    /// <summary>Runs <paramref name="action"/> in a new scope, in the Elsa tenant <paramref name="elsaTenantId"/> when given.</summary>
    public async Task<T> InTenantAsync<T>(string? elsaTenantId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantAccessor>();
        using var tenant = elsaTenantId is null ? null : tenants.PushContext(new Tenant { Id = elsaTenantId, Name = elsaTenantId });
        return await action(scope.ServiceProvider);
    }

    public Task InTenantAsync(string? elsaTenantId, Func<IServiceProvider, Task> action) =>
        InTenantAsync<object?>(elsaTenantId, async provider =>
        {
            await action(provider);
            return null;
        });

    /// <summary>Runs <paramref name="action"/> with Elsa's <see cref="IAgentStore"/> in a new scope, in Elsa's default tenant.</summary>
    public Task<T> WithStoreAsync<T>(Func<IAgentStore, Task<T>> action) =>
        InTenantAsync(null, provider => action(provider.GetRequiredService<IAgentStore>()));

    public Task WithStoreAsync(Func<IAgentStore, Task> action) =>
        InTenantAsync(null, provider => action(provider.GetRequiredService<IAgentStore>()));

    /// <summary>An Aster agent store on this host with other options, such as another channel.</summary>
    public AsterAgentStore CreateStore(IServiceProvider scope, AsterAgentStoreOptions options) =>
        ActivatorUtilities.CreateInstance<AsterAgentStore>(scope, Options.Create(options));

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        if (database is not null && ownsDatabase)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }

        serverDatabase?.Dispose();
    }
}

/// <summary>Agent definitions for tests.</summary>
internal static class TestAgents
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>An agent with every field of Elsa's agent model set.</summary>
    public static AgentDefinition Create(string id, string name, params string[] agents) => new()
    {
        Id = id,
        Name = name,
        Description = $"The {name} agent",
        AgentConfig = new AgentConfig
        {
            Name = name,
            FunctionName = $"{name}Function",
            Description = $"Configuration of {name}",
            PromptTemplate = $"You are {name}. Answer questions about {{{{topic}}}}.",
            InputVariables = [new InputVariableConfig { Name = "topic", Description = "The topic", Type = "String" }],
            OutputVariable = new OutputVariableConfig { Description = "The answer", Type = "String" },
            ExecutionSettings = new ExecutionSettingsConfig { MaxTokens = 500, Temperature = 0.2, TopP = 0.9, PresencePenalty = 0.1, FrequencyPenalty = 0.3, ResponseFormat = "json_object" },
            Skills = ["ImageGenerator", "DocumentQuery"],
            Agents = [.. agents],
        },
    };

    /// <summary>The agent as JSON without its tenant ID, to compare everything the store must round-trip.</summary>
    public static string Snapshot(AgentDefinition? agent)
    {
        Assert.NotNull(agent);
        var tenantId = agent.TenantId;
        agent.TenantId = null;
        var json = JsonSerializer.Serialize(agent, Json);
        agent.TenantId = tenantId;
        return json;
    }
}
