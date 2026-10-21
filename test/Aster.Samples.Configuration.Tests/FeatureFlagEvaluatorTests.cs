using Microsoft.Extensions.Options;
using Aster.Samples.Configuration.App;

namespace Aster.Samples.Configuration.Tests;

public sealed class FeatureFlagEvaluatorTests
{
    private static FeatureFlagEvaluator Evaluator(FeatureFlagSettings flag) =>
        new(new StaticOptionsMonitor(new FeatureFlagsOptions { ["checkout"] = flag }));

    [Fact]
    public void IsEnabled_UnknownOrDisabledFlag_IsOff()
    {
        var evaluator = Evaluator(new FeatureFlagSettings { Enabled = false });

        Assert.False(evaluator.IsEnabled("checkout", "acme", "user-1"));
        Assert.False(evaluator.IsEnabled("unknown", "acme", "user-1"));
    }

    [Fact]
    public void IsEnabled_FlagLimitedToTenants_IsOffForOtherTenants()
    {
        var evaluator = Evaluator(new FeatureFlagSettings { Enabled = true, AllowedTenants = ["acme"] });

        Assert.True(evaluator.IsEnabled("checkout", "acme", "user-1"));
        Assert.True(evaluator.IsEnabled("CHECKOUT", "acme", "user-1"));
        Assert.False(evaluator.IsEnabled("checkout", "globex", "user-1"));
        Assert.False(evaluator.IsEnabled("checkout", tenantId: null, "user-1"));
    }

    [Fact]
    public void IsEnabled_PartialRollout_IsOnForAStableShareOfSubjects()
    {
        var evaluator = Evaluator(new FeatureFlagSettings { Enabled = true, RolloutPercentage = 25 });
        var subjects = Enumerable.Range(0, 2000).Select(static index => $"user-{index}").ToList();

        var enabled = subjects.Where(subject => evaluator.IsEnabled("checkout", "acme", subject)).ToList();

        Assert.InRange(enabled.Count, 400, 600);
        Assert.Equal(enabled, subjects.Where(subject => evaluator.IsEnabled("checkout", "acme", subject)));
        Assert.All(enabled, static subject => Assert.InRange(FeatureFlagEvaluator.Bucket("checkout", subject), 0, 24));
        Assert.False(evaluator.IsEnabled("checkout", "acme", subjectId: null));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void IsEnabled_ZeroOrFullRollout_IgnoresTheSubject(int percentage, bool expected)
    {
        var evaluator = Evaluator(new FeatureFlagSettings { Enabled = true, RolloutPercentage = percentage });

        Assert.Equal(expected, evaluator.IsEnabled("checkout", "acme", "user-1"));
        Assert.Equal(expected, evaluator.IsEnabled("checkout", "acme", subjectId: null));
    }

    private sealed class StaticOptionsMonitor(FeatureFlagsOptions value) : IOptionsMonitor<FeatureFlagsOptions>
    {
        public FeatureFlagsOptions CurrentValue => value;

        public FeatureFlagsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<FeatureFlagsOptions, string?> listener) => null;
    }
}
