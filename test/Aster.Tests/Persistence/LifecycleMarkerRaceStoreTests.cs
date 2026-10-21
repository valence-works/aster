using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Aster.Tests.Policies;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

/// <summary>
/// Two marker writes on one resource that both pass the "no marker yet" check (#238): the store writes only the first,
/// the other gets the result the check would have given it, and only the winner's after-archive hooks see a success.
/// </summary>
public abstract class LifecycleMarkerRaceStoreTests(IAsterTestDatabase database) : IDisposable
{
    private static readonly DateTimeOffset MarkedAt = new(2026, 5, 27, 0, 0, 0, TimeSpan.Zero);

    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task ArchiveAndSoftDeleteRace_OneAppliesAndTheOtherConflicts()
    {
        await using var provider = await CreateProviderAsync();
        var hook = provider.GetRequiredService<RaceGateHook>();

        var results = await RaceAsync(
            provider,
            (ResourceLifecycleMarkerState.Archived, "archive"),
            (ResourceLifecycleMarkerState.SoftDeleted, "soft-delete"));

        var winner = Assert.Single(results, static result => result.Succeeded);
        var loser = Assert.Single(results, static result => !result.Succeeded);
        var stored = await provider.GetRequiredService<IResourceLifecycleMarkerService>().GetAsync("product-1");
        Assert.Equal(winner.Marker, stored);
        Assert.Equal(stored, loser.Marker);
        var diagnostic = Assert.Single(loser.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict, diagnostic.Code);
        Assert.Equal("product-1", diagnostic.ResourceId);

        AssertOnlyTheWinnerSucceeded(hook, stored!);
    }

    [SkippableFact]
    public async Task TwoArchivesRace_OneAppliesAndTheOtherIsAlreadySatisfied()
    {
        await using var provider = await CreateProviderAsync();
        var hook = provider.GetRequiredService<RaceGateHook>();

        // The public service reports Applied and AlreadySatisfied alike as success, so this race asserts the
        // transition statuses underneath it.
        var results = await TransitionRaceAsync(
            provider,
            (ResourceLifecycleMarkerState.Archived, "first"),
            (ResourceLifecycleMarkerState.Archived, "second"));

        var stored = await provider.GetRequiredService<IResourceLifecycleMarkerService>().GetAsync("product-1");
        Assert.NotNull(stored);
        Assert.Single(results, static result => result.Status == ResourceLifecycleMarkerTransitionStatus.Applied);
        Assert.Single(results, static result => result.Status == ResourceLifecycleMarkerTransitionStatus.AlreadySatisfied);
        Assert.All(results, result =>
        {
            Assert.Empty(result.Diagnostics);
            Assert.Equal(stored, result.Marker);
        });

        AssertOnlyTheWinnerSucceeded(hook, stored);
    }

    [SkippableFact]
    public async Task CreateMarkerAsync_WritesOnlyWhenTheResourceHasNoMarkerInItsTenant()
    {
        await using var provider = Database.CreateProvider();
        var store = provider.GetRequiredService<IResourceLifecycleMarkerStore>();

        var first = await store.CreateMarkerAsync(Marker(ResourceLifecycleMarkerState.Archived, "first", TenantScopeTestFixtures.TenantA));
        var second = await store.CreateMarkerAsync(Marker(ResourceLifecycleMarkerState.SoftDeleted, "second", TenantScopeTestFixtures.TenantA));
        var otherTenant = await store.CreateMarkerAsync(Marker(ResourceLifecycleMarkerState.SoftDeleted, "other", TenantScopeTestFixtures.TenantB));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Marker, second.Marker);
        Assert.Equal(first.Marker, await store.GetMarkerAsync("product-1", TenantScopeTestFixtures.TenantA));
        Assert.True(otherTenant.Created);
        Assert.Equal(otherTenant.Marker, await store.GetMarkerAsync("product-1", TenantScopeTestFixtures.TenantB));
    }

    private static ResourceLifecycleMarker Marker(ResourceLifecycleMarkerState state, string reason, TenantScope tenant) =>
        new()
        {
            TenantScope = tenant,
            ResourceId = "product-1",
            State = state,
            MarkedAt = MarkedAt,
            Reason = reason,
        };

    private async Task<ServiceProvider> CreateProviderAsync()
    {
        var provider = Database.CreateProvider(static services => services.AddResourceLifecycleHook<RaceGateHook>());
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        return provider;
    }

    // Both writes read "no marker" before either reaches the store: the gate holds each in its before-archive hook,
    // which runs after the check, until both have arrived.
    private static async Task<ResourceLifecycleMarkerResult[]> RaceAsync(
        IServiceProvider provider,
        params (ResourceLifecycleMarkerState State, string Reason)[] writes)
    {
        var markers = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        provider.GetRequiredService<RaceGateHook>().Expect(writes.Length);

        return await Task.WhenAll(writes.Select(write => Task.Run(async () => await markers.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = write.State,
            Reason = write.Reason,
            MarkedAt = MarkedAt,
        }))));
    }

    private static async Task<ResourceLifecycleMarkerTransitionResult[]> TransitionRaceAsync(
        IServiceProvider provider,
        params (ResourceLifecycleMarkerState State, string Reason)[] writes)
    {
        var transition = provider.GetRequiredService<IResourceLifecycleMarkerTransitionService>();
        provider.GetRequiredService<RaceGateHook>().Expect(writes.Length);

        return await Task.WhenAll(writes.Select(write => Task.Run(async () => await transition.ApplyAsync(new ResourceLifecycleMarkerTransitionApplyRequest
        {
            TenantScope = TenantScope.Default,
            ResourceId = "product-1",
            State = write.State,
            Reason = write.Reason,
            MarkedAt = MarkedAt,
        }))));
    }

    private static void AssertOnlyTheWinnerSucceeded(RaceGateHook hook, ResourceLifecycleMarker stored)
    {
        Assert.Equal(2, hook.AfterArchive.Count);
        var succeeded = Assert.Single(hook.AfterArchive, static context => context.OperationStatus == LifecycleOperationStatus.Succeeded);
        Assert.Equal(stored, succeeded.Marker);
        var failed = Assert.Single(hook.AfterArchive, static context => context.OperationStatus == LifecycleOperationStatus.Failed);
        Assert.NotEqual(stored.Reason, failed.Marker.Reason);
        Assert.Equal(LifecycleOperationFailure.OperationFailedCode, failed.OperationFailure?.Code);
        Assert.NotEqual(succeeded.OperationId, failed.OperationId);
    }
}

public sealed class InMemoryLifecycleMarkerRaceStoreTests() : LifecycleMarkerRaceStoreTests(new InMemoryRaceTestDatabase());

public sealed class SqliteJsonLifecycleMarkerRaceStoreTests() : LifecycleMarkerRaceStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbLifecycleMarkerRaceStoreTests() : LifecycleMarkerRaceStoreTests(new PostgresTestDatabase());

public sealed class SqlServerJsonLifecycleMarkerRaceStoreTests() : LifecycleMarkerRaceStoreTests(new SqlServerTestDatabase());

/// <summary>
/// Holds every before-archive call until the expected number have arrived, and records the after-archive calls.
/// </summary>
public sealed class RaceGateHook : ResourceLifecycleHook
{
    private readonly object gate = new();
    private readonly List<ResourceArchiveLifecycleContext> afterArchive = [];
    private TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int remaining;

    public IReadOnlyList<ResourceArchiveLifecycleContext> AfterArchive
    {
        get
        {
            lock (gate)
                return afterArchive.ToList();
        }
    }

    public void Expect(int writers)
    {
        lock (gate)
        {
            remaining = writers;
            allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public override async ValueTask<LifecycleHookOutcome> OnBeforeArchiveAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        Task arrived;
        lock (gate)
        {
            if (--remaining == 0)
                allArrived.TrySetResult();
            arrived = allArrived.Task;
        }

        // Bounded so a write that never reaches the hook fails the test instead of hanging it.
        await arrived.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return LifecycleHookOutcome.Continue();
    }

    public override ValueTask OnAfterArchiveAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
            afterArchive.Add(context);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The in-memory provider. Each provider it creates starts empty.
/// </summary>
file sealed class InMemoryRaceTestDatabase : IAsterTestDatabase
{
    public ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddAsterCore();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
    }
}
