using static Aster.Samples.Configuration.Tests.ConfigurationStoreFixture;

namespace Aster.Samples.Configuration.Tests;

/// <summary>
/// Runs the walkthrough that <c>dotnet run</c> prints, so the sample cannot rot unnoticed.
/// </summary>
public sealed class ConfigurationScenarioTests
{
    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RunAsync_CompletesTheWalkthrough(string provider)
    {
        await using var store = await CreateAsync(provider, registerDefinitions: false);
        using var output = new StringWriter();

        await ConfigurationScenario.RunAsync(store.Services, output);

        var lines = output.ToString().Split(Environment.NewLine);
        Assert.Contains("    [prod] configuration reloaded", lines);
        Assert.Contains("    feature-flag.new-checkout v2: dev, prod, staging", lines);
        Assert.Contains("    app-settings.checkout v1: prod", lines);
        Assert.Contains("    app-settings.checkout v2: dev, staging", lines);
        Assert.Contains(lines, static line => line.StartsWith("    prod ", StringComparison.Ordinal) && line.EndsWith("checkout: legacy-gateway, 3 attempt(s), [EUR, USD]", StringComparison.Ordinal));
    }
}
