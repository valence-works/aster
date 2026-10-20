using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Lifecycle;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

/// <summary>
/// #127: activation and deactivation hook contexts carry the resource's DefinitionId, so hooks
/// (for example Elsa triggers) can filter by resource type without reading the resource.
/// </summary>
public sealed class LifecycleActivationDefinitionIdTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider BuildServices(string providerKind)
    {
        var services = new ServiceCollection()
            .AddSingleton<LifecycleHookRecorder>()
            .AddAsterCore()
            .AddResourceLifecycleHook<FirstRecordingHook>();

        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static IEnumerable<ResourceActivationLifecycleContext> ActivationContexts(LifecycleHookRecorder recorder) =>
        recorder.Events.Select(static recorded => recorded.Context).OfType<ResourceActivationLifecycleContext>();

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ActivateAndDeactivate_ContextsCarryDefinitionId(string providerKind)
    {
        await using var provider = BuildServices(providerKind);
        var manager = provider.GetRequiredService<IResourceManager>();
        var recorder = provider.GetRequiredService<LifecycleHookRecorder>();
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var resource = await manager.CreateAsync(LifecycleHookTestFixtures.DefinitionId, LifecycleHookTestFixtures.CreateRequest());
        recorder.Events.Clear();

        await manager.ActivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel);
        await manager.DeactivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel);

        Assert.Equal(
            [
                (LifecyclePoint.BeforeActivate, LifecycleHookTestFixtures.DefinitionId),
                (LifecyclePoint.AfterActivate, LifecycleHookTestFixtures.DefinitionId),
                (LifecyclePoint.BeforeDeactivate, LifecycleHookTestFixtures.DefinitionId),
                (LifecyclePoint.AfterDeactivate, LifecycleHookTestFixtures.DefinitionId),
            ],
            ActivationContexts(recorder).Select(static context => (context.LifecyclePoint, context.DefinitionId)));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task HistoricalActivation_ContextCarriesDefinitionId(string providerKind)
    {
        await using var provider = BuildServices(providerKind);
        var manager = provider.GetRequiredService<IResourceManager>();
        var recorder = provider.GetRequiredService<LifecycleHookRecorder>();
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var resource = await manager.CreateAsync(LifecycleHookTestFixtures.DefinitionId, LifecycleHookTestFixtures.CreateRequest());
        await manager.UpdateAsync(resource.ResourceId, LifecycleHookTestFixtures.UpdateRequest(resource.Version));
        recorder.Events.Clear();

        await manager.ActivateAsync(resource.ResourceId, version: 1, LifecycleHookTestFixtures.Channel);

        Assert.All(
            ActivationContexts(recorder),
            static context =>
            {
                Assert.Equal(1, context.Version);
                Assert.Equal(LifecycleHookTestFixtures.DefinitionId, context.DefinitionId);
            });
    }

    [Fact]
    public async Task DeactivateUnknownResource_ContextHasNoDefinitionId()
    {
        await using var provider = BuildServices(InMemory);
        var manager = provider.GetRequiredService<IResourceManager>();
        var recorder = provider.GetRequiredService<LifecycleHookRecorder>();

        await manager.DeactivateAsync("missing", 1, LifecycleHookTestFixtures.Channel);

        Assert.All(ActivationContexts(recorder), static context => Assert.Null(context.DefinitionId));
        Assert.NotEmpty(ActivationContexts(recorder));
    }
}
