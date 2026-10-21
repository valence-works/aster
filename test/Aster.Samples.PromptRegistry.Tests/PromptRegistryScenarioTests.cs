namespace Aster.Samples.PromptRegistry.Tests;

/// <summary>
/// Runs the walkthrough that <c>dotnet run</c> prints, so the sample cannot rot unnoticed.
/// </summary>
public sealed class PromptRegistryScenarioTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_CompletesTheWalkthrough(bool useSqlite)
    {
        using var output = new StringWriter();

        await PromptRegistryScenario.RunAsync(useSqlite, output);

        var lines = output.ToString().Split(Environment.NewLine);
        Assert.Contains("    production has prompt versions [1, 2] active", lines);
        Assert.Contains("    prompt.support-triage  v1 inactive | v2 candidate+production", lines);
        Assert.Contains("    import: Imported", lines);
        Assert.Contains(lines, static line => line.StartsWith("    us production resolves agent.support v1 -> prompt.support-triage__imported v2", StringComparison.Ordinal));
        Assert.Contains("    the us team's own prompt.support-triage is untouched: \"Help the customer with {{request}}.\"", lines);
    }
}
