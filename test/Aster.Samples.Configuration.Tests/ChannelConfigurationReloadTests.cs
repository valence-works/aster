using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Aster.Samples.Configuration.App;
using Aster.Samples.Configuration.Model;
using static Aster.Samples.Configuration.Tests.ConfigurationStoreFixture;

namespace Aster.Samples.Configuration.Tests;

/// <summary>
/// An application reads the configuration active in its environment's channel through
/// <see cref="IConfiguration"/> and <see cref="IOptionsMonitor{TOptions}"/>, and the reload hook
/// refreshes it during every promotion, rollback or deactivation in that channel.
/// </summary>
public sealed class ChannelConfigurationReloadTests
{
    private static readonly string FlagId = ConfigurationDefinitions.FeatureFlagResourceId(ConfigurationScenario.NewCheckoutFlag);
    private static readonly string CheckoutId = ConfigurationDefinitions.AppSettingsResourceId(CheckoutOptions.Section);

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task OptionsMonitor_FollowsPromotionAndRollback(string provider)
    {
        await using var store = await CreateAsync(provider);
        await using var prod = EnvironmentApplication.Start(store.Services, Environments.Prod);
        var reloads = new List<string>();
        using var subscription = prod.Checkout.OnChange(options => reloads.Add(options.PaymentProvider));

        Assert.Equal("none", prod.Checkout.CurrentValue.PaymentProvider);
        Assert.False(prod.Flags.IsEnabled(ConfigurationScenario.NewCheckoutFlag, "acme", "user-1"));

        // Promote v1 to prod: the reload has happened by the time the promotion returns.
        await store.ReleaseEverywhereAsync(ConfigurationScenario.CheckoutV1());
        Assert.Equal(new[] { "legacy-gateway" }, reloads);
        var checkout = prod.Checkout.CurrentValue;
        Assert.Equal(("legacy-gateway", 3), (checkout.PaymentProvider, checkout.Retry.MaxAttempts));
        Assert.Equal(new[] { "EUR", "USD" }, checkout.Currencies);

        // Promote v2 to prod.
        await store.ReleaseEverywhereAsync(ConfigurationScenario.CheckoutV2());
        Assert.Equal(new[] { "legacy-gateway", "stripe" }, reloads);
        checkout = prod.Checkout.CurrentValue;
        Assert.Equal(("stripe", 5), (checkout.PaymentProvider, checkout.Retry.MaxAttempts));
        Assert.Equal(new[] { "EUR", "USD", "GBP" }, checkout.Currencies);

        // Roll prod back to v1.
        await store.Releases.RollbackAsync(CheckoutId, Environments.Prod, 1);
        Assert.Equal(new[] { "legacy-gateway", "stripe", "legacy-gateway" }, reloads);
        checkout = prod.Checkout.CurrentValue;
        Assert.Equal(("legacy-gateway", 3), (checkout.PaymentProvider, checkout.Retry.MaxAttempts));
        Assert.Equal(new[] { "EUR", "USD" }, checkout.Currencies);
        Assert.Null(prod.Configuration["Checkout:Currencies:2"]);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task FeatureFlags_FollowPromotionAndRollback(string provider)
    {
        await using var store = await CreateAsync(provider);
        await using var prod = EnvironmentApplication.Start(store.Services, Environments.Prod);
        var globexUser = SubjectInsideHalfRollout();

        await store.ReleaseEverywhereAsync(ConfigurationScenario.NewCheckoutV1);
        Assert.Equal(new[] { "acme" }, prod.Services.GetRequiredService<IOptionsMonitor<FeatureFlagsOptions>>().CurrentValue[ConfigurationScenario.NewCheckoutFlag].AllowedTenants);
        Assert.False(prod.Flags.IsEnabled(ConfigurationScenario.NewCheckoutFlag, "globex", globexUser));

        await store.ReleaseEverywhereAsync(ConfigurationScenario.NewCheckoutV2);
        Assert.True(prod.Flags.IsEnabled(ConfigurationScenario.NewCheckoutFlag, "globex", globexUser));
        Assert.Equal("50", prod.Configuration["FeatureFlags:new-checkout:RolloutPercentage"]);

        await store.Releases.RollbackAsync(FlagId, Environments.Prod, 1);
        Assert.False(prod.Flags.IsEnabled(ConfigurationScenario.NewCheckoutFlag, "globex", globexUser));
        Assert.Equal("10", prod.Configuration["FeatureFlags:new-checkout:RolloutPercentage"]);
        Assert.Null(prod.Configuration["FeatureFlags:new-checkout:AllowedTenants:1"]);
    }

    [Fact]
    public async Task ActivationsInOtherChannels_DoNotReloadTheApplication()
    {
        await using var store = await CreateAsync(InMemory);
        await using var prod = EnvironmentApplication.Start(store.Services, Environments.Prod);
        await using var staging = EnvironmentApplication.Start(store.Services, Environments.Staging);
        var prodReloads = 0;
        var stagingReloads = 0;
        using var prodSubscription = prod.Checkout.OnChange(_ => prodReloads++);
        using var stagingSubscription = staging.Checkout.OnChange(_ => stagingReloads++);

        await store.Releases.SaveAsync(ConfigurationScenario.CheckoutV1());
        await store.Releases.ReleaseAsync(CheckoutId, 1, Environments.Dev);
        await store.Releases.PromoteAsync(CheckoutId, Environments.Dev);

        Assert.Equal(0, prodReloads);
        Assert.Equal(1, stagingReloads);
        Assert.Equal("none", prod.Checkout.CurrentValue.PaymentProvider);
        Assert.Equal("legacy-gateway", staging.Checkout.CurrentValue.PaymentProvider);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Deactivation_RemovesTheConfiguration(string provider)
    {
        await using var store = await CreateAsync(provider);
        await using var prod = EnvironmentApplication.Start(store.Services, Environments.Prod);
        await store.ReleaseEverywhereAsync(ConfigurationScenario.NewCheckoutV2);
        Assert.NotNull(prod.Configuration["FeatureFlags:new-checkout:Enabled"]);

        await store.Resources.DeactivateAsync(FlagId, 1, Environments.Prod);

        Assert.Null(prod.Configuration["FeatureFlags:new-checkout:Enabled"]);
        Assert.False(prod.Flags.IsEnabled(ConfigurationScenario.NewCheckoutFlag, "acme", "user-1"));
    }

    [Fact]
    public async Task StartedAfterRelease_LoadsTheActiveConfiguration()
    {
        await using var store = await CreateAsync(Sqlite);
        await store.ReleaseEverywhereAsync(ConfigurationScenario.CheckoutV2());

        await using var prod = EnvironmentApplication.Start(store.Services, Environments.Prod);

        Assert.Equal("stripe", prod.Checkout.CurrentValue.PaymentProvider);
    }

    [Fact]
    public async Task StoppedApplication_NoLongerListens()
    {
        await using var store = await CreateAsync(InMemory);
        var prod = EnvironmentApplication.Start(store.Services, Environments.Prod);
        await prod.DisposeAsync();

        // The disposed provider has unsubscribed, so the activation neither reloads it nor fails.
        await store.ReleaseEverywhereAsync(ConfigurationScenario.CheckoutV1());

        Assert.Equal(1, await store.Releases.GetActiveVersionAsync(CheckoutId, Environments.Prod));
    }

    // A globex user inside a 50% rollout.
    private static string SubjectInsideHalfRollout() =>
        Enumerable.Range(0, 1000)
            .Select(static index => $"user-{index}")
            .First(static subject => FeatureFlagEvaluator.Bucket(ConfigurationScenario.NewCheckoutFlag, subject) < 50);
}
