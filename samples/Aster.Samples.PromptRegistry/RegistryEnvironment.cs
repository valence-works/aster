using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Persistence.SqliteJson;
using Aster.Samples.PromptRegistry.Model;
using Aster.Samples.PromptRegistry.Portability;
using Aster.Samples.PromptRegistry.Registry;

namespace Aster.Samples.PromptRegistry;

/// <summary>
/// One deployment of the registry with its own Aster store: in memory by default, or a SQLite file.
/// </summary>
public sealed class RegistryEnvironment : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly string? databasePath;

    private RegistryEnvironment(string name, ServiceProvider services, string? databasePath)
    {
        Name = name;
        this.services = services;
        this.databasePath = databasePath;
    }

    /// <summary>The environment name, such as <c>eu</c>.</summary>
    public string Name { get; }

    /// <summary>The Aster services of this environment.</summary>
    public IServiceProvider Services => services;

    /// <summary>The registry API.</summary>
    public PromptRegistryService Registry => new(services.GetRequiredService<IResourceManager>(), services.GetRequiredService<ITypedAspectBinder>());

    /// <summary>Export and import of prompt sets.</summary>
    public PromptSetTransfer Transfer => new(services.GetRequiredService<IResourcePortabilityService>());

    /// <summary>
    /// Starts an environment and registers the registry's definitions in it.
    /// </summary>
    /// <param name="name">The environment name.</param>
    /// <param name="useSqlite">Whether to store data in a temporary SQLite file, deleted on dispose.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task<RegistryEnvironment> StartAsync(string name, bool useSqlite = false, CancellationToken cancellationToken = default)
    {
        var databasePath = useSqlite ? Path.Combine(Path.GetTempPath(), $"aster-prompt-registry-{name}-{Guid.NewGuid():N}.db") : null;
        var services = new ServiceCollection().AddAsterCore();
        if (databasePath is not null)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        var environment = new RegistryEnvironment(name, services.BuildServiceProvider(), databasePath);
        await RegistryDefinitions.RegisterAsync(environment.services.GetRequiredService<IResourceDefinitionStore>(), cancellationToken);
        return environment;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        if (databasePath is null)
            return;

        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            File.Delete(file);
    }
}
