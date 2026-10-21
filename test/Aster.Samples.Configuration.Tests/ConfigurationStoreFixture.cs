using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Aster.Core.Abstractions;
using Aster.Samples.Configuration.Model;
using Aster.Samples.Configuration.Promotion;

namespace Aster.Samples.Configuration.Tests;

/// <summary>
/// A configuration store on the in-memory or the SQLite provider, with the sample's definitions registered.
/// </summary>
internal sealed class ConfigurationStoreFixture : IAsyncDisposable
{
    public const string InMemory = "in-memory";
    public const string Sqlite = "sqlite";

    private readonly string? databasePath;

    private ConfigurationStoreFixture(string? databasePath, ServiceProvider services)
    {
        this.databasePath = databasePath;
        Services = services;
        Releases = new ConfigurationReleases(services.GetRequiredService<IResourceManager>());
    }

    public ServiceProvider Services { get; }

    public ConfigurationReleases Releases { get; }

    public IResourceManager Resources => Services.GetRequiredService<IResourceManager>();

    public static async Task<ConfigurationStoreFixture> CreateAsync(string provider, bool registerDefinitions = true)
    {
        var databasePath = provider == Sqlite ? Path.Combine(Path.GetTempPath(), $"aster-configuration-tests-{Guid.NewGuid():N}.db") : null;
        var fixture = new ConfigurationStoreFixture(databasePath, ConfigurationStore.Build(databasePath));
        if (registerDefinitions)
            await ConfigurationDefinitions.RegisterAsync(fixture.Services.GetRequiredService<IResourceDefinitionStore>());

        return fixture;
    }

    /// <summary>Saves a new version and promotes it from dev all the way to prod.</summary>
    public async Task<int> ReleaseEverywhereAsync(FeatureFlag flag) =>
        await ReleaseEverywhereAsync(ConfigurationDefinitions.FeatureFlagResourceId(flag.Key), (await Releases.SaveAsync(flag)).Version);

    /// <summary>Saves a new version and promotes it from dev all the way to prod.</summary>
    public async Task<int> ReleaseEverywhereAsync(AppSettings settings) =>
        await ReleaseEverywhereAsync(ConfigurationDefinitions.AppSettingsResourceId(settings.Section), (await Releases.SaveAsync(settings)).Version);

    private async Task<int> ReleaseEverywhereAsync(string resourceId, int version)
    {
        await Releases.ReleaseAsync(resourceId, version, Environments.Dev);
        await Releases.PromoteAsync(resourceId, Environments.Dev);
        await Releases.PromoteAsync(resourceId, Environments.Staging);
        return version;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (databasePath is null)
            return;

        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            File.Delete(file);
    }
}
