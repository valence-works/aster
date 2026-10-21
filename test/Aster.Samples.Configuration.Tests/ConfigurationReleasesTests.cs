using Aster.Core.Exceptions;
using Aster.Samples.Configuration.Model;
using static Aster.Samples.Configuration.Tests.ConfigurationStoreFixture;

namespace Aster.Samples.Configuration.Tests;

/// <summary>
/// Promotion moves a version from one environment channel to the next; rollback re-activates an
/// older version in one channel through historical activation without rewriting anything.
/// </summary>
public sealed class ConfigurationReleasesTests
{
    private static readonly string FlagId = ConfigurationDefinitions.FeatureFlagResourceId(ConfigurationScenario.NewCheckoutFlag);

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task PromoteAsync_MovesTheVersionFromDevToStagingToProd(string provider)
    {
        await using var store = await CreateAsync(provider);
        await store.Releases.SaveAsync(ConfigurationScenario.NewCheckoutV1);
        await store.Releases.ReleaseAsync(FlagId, 1, Environments.Dev);

        Assert.Equal(1, await store.Releases.PromoteAsync(FlagId, Environments.Dev));
        Assert.Null(await store.Releases.GetActiveVersionAsync(FlagId, Environments.Prod));

        Assert.Equal(1, await store.Releases.PromoteAsync(FlagId, Environments.Staging));

        foreach (var environment in Environments.PromotionOrder)
            Assert.Equal(1, await store.Releases.GetActiveVersionAsync(FlagId, environment));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task PromoteAsync_ReplacesTheVersionActiveInTheNextEnvironment(string provider)
    {
        await using var store = await CreateAsync(provider);
        await store.ReleaseEverywhereAsync(ConfigurationScenario.NewCheckoutV1);

        await store.Releases.SaveAsync(ConfigurationScenario.NewCheckoutV2);
        await store.Releases.ReleaseAsync(FlagId, 2, Environments.Dev);
        await store.Releases.PromoteAsync(FlagId, Environments.Dev);

        Assert.Equal(2, await store.Releases.GetActiveVersionAsync(FlagId, Environments.Staging));
        Assert.Equal(1, await store.Releases.GetActiveVersionAsync(FlagId, Environments.Prod));
        Assert.Equal([2], (await store.Resources.GetActiveVersionsAsync(FlagId, Environments.Staging)).Select(static resource => resource.Version));
    }

    [Fact]
    public async Task PromoteAsync_WithNothingActive_Throws()
    {
        await using var store = await CreateAsync(InMemory);
        await store.Releases.SaveAsync(ConfigurationScenario.NewCheckoutV1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Releases.PromoteAsync(FlagId, Environments.Dev));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Releases.PromoteAsync(FlagId, Environments.Prod));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RollbackAsync_ReactivatesTheOlderVersionInThatEnvironmentOnly(string provider)
    {
        await using var store = await CreateAsync(provider);
        await store.ReleaseEverywhereAsync(ConfigurationScenario.NewCheckoutV1);
        await store.ReleaseEverywhereAsync(ConfigurationScenario.NewCheckoutV2);

        var previous = await store.Releases.RollbackAsync(FlagId, Environments.Prod, 1);

        Assert.Equal(2, previous);
        Assert.Equal(1, await store.Releases.GetActiveVersionAsync(FlagId, Environments.Prod));
        Assert.Equal(2, await store.Releases.GetActiveVersionAsync(FlagId, Environments.Staging));

        // Historical activation rewrites nothing: version 2 is still the latest and both versions keep their content.
        Assert.Equal(2, (await store.Resources.GetLatestVersionAsync(FlagId))!.Version);
        Assert.Equal([1, 2], (await store.Resources.GetVersionsAsync(FlagId)).Select(static resource => resource.Version).Order());
    }

    [Fact]
    public async Task RollbackAsync_ToTheActiveOrANewerVersion_Throws()
    {
        await using var store = await CreateAsync(InMemory);
        await store.ReleaseEverywhereAsync(ConfigurationScenario.NewCheckoutV1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Releases.RollbackAsync(FlagId, Environments.Prod, 1));
    }

    [Fact]
    public async Task SaveAsync_EnforcesTheDefinitionConstraints()
    {
        await using var store = await CreateAsync(InMemory);

        await Assert.ThrowsAsync<FacetValueValidationException>(() =>
            store.Releases.SaveAsync(ConfigurationScenario.NewCheckoutV1 with { RolloutPercentage = 150 }));
        await Assert.ThrowsAsync<FacetValueValidationException>(() =>
            store.Releases.SaveAsync(ConfigurationScenario.NewCheckoutV1 with { AllowedTenants = ["acme", "acme"] }));
        await Assert.ThrowsAsync<FacetValueValidationException>(() =>
            store.Releases.SaveAsync(ConfigurationScenario.CheckoutV1() with { Section = ChannelConfiguration.ChannelConfigurationLoader.FeatureFlagsSection }));
    }
}
