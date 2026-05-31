using Aster.Core.Models.Lifecycle;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleHookOutcomeSummaryTests
{
    [Fact]
    public void Summary_SingleContinueOutcome_ReturnsSuccessfulCounts()
    {
        var summary = LifecycleHookOutcome.Continue().ToSummary();

        Assert.True(summary.IsFullySuccessful);
        Assert.False(summary.HasRejectedOutcomes);
        Assert.False(summary.HasFailedOutcomes);
        Assert.False(summary.HasDiagnostics);
        Assert.Equal(1, summary.TotalOutcomeCount);
        Assert.Equal(1, summary.ContinueCount);
        Assert.Equal(0, summary.RejectedCount);
        Assert.Equal(0, summary.FailedCount);
        Assert.Equal(0, summary.TotalDiagnosticCount);
        Assert.Equal(
            [(LifecycleHookOutcomeStatus.Continue, 1)],
            summary.StatusCounts.Select(static count => (count.Status, count.Count)).ToList());
    }

    [Fact]
    public void Summary_MixedOutcomes_AggregatesStatusAndOutcomeCodesDeterministically()
    {
        var summary = new[]
        {
            LifecycleHookOutcome.Fail("z-failed", "Failed."),
            LifecycleHookOutcome.Continue(),
            LifecycleHookOutcome.Reject("a-rejected", "Rejected."),
            LifecycleHookOutcome.Fail("z-failed", "Failed again."),
        }.ToSummary();

        Assert.False(summary.IsFullySuccessful);
        Assert.True(summary.HasRejectedOutcomes);
        Assert.True(summary.HasFailedOutcomes);
        Assert.Equal(4, summary.TotalOutcomeCount);
        Assert.Equal(1, summary.ContinueCount);
        Assert.Equal(1, summary.RejectedCount);
        Assert.Equal(2, summary.FailedCount);
        Assert.Equal(
            [
                (LifecycleHookOutcomeStatus.Continue, 1),
                (LifecycleHookOutcomeStatus.Rejected, 1),
                (LifecycleHookOutcomeStatus.Failed, 2),
            ],
            summary.StatusCounts.Select(static count => (count.Status, count.Count)).ToList());
        Assert.Equal(
            [("a-rejected", 1), ("z-failed", 2)],
            summary.OutcomeCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void Summary_NullSingleOutcome_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((LifecycleHookOutcome)null!).ToSummary());
    }

    [Fact]
    public void Summary_NullAndEmptyEnumerable_ReturnZeroCounts()
    {
        var nullSummary = ((IEnumerable<LifecycleHookOutcome>?)null).ToSummary();
        var emptySummary = Array.Empty<LifecycleHookOutcome>().ToSummary();

        Assert.True(nullSummary.IsFullySuccessful);
        Assert.Equal(0, nullSummary.TotalOutcomeCount);
        Assert.Empty(nullSummary.StatusCounts);

        Assert.True(emptySummary.IsFullySuccessful);
        Assert.Equal(0, emptySummary.TotalOutcomeCount);
        Assert.Empty(emptySummary.StatusCounts);
    }

    [Fact]
    public void Summary_Diagnostics_AggregatesCodeLifecyclePointAndHookTypeDeterministically()
    {
        var summary = new[]
        {
            LifecycleHookOutcome.Reject(
                "rejected",
                "Rejected.",
                [
                    Diagnostic("z-diagnostic", LifecyclePoint.BeforeSave, "ZHook"),
                    Diagnostic("a-diagnostic", LifecyclePoint.AfterSave, "AHook"),
                    Diagnostic("z-diagnostic", LifecyclePoint.BeforeSave, "AHook"),
                ]),
            LifecycleHookOutcome.Fail(
                "failed",
                "Failed.",
                [
                    Diagnostic("a-diagnostic", LifecyclePoint.AfterImport, "ZHook"),
                ]),
        }.ToSummary();

        Assert.True(summary.HasDiagnostics);
        Assert.Equal(4, summary.TotalDiagnosticCount);
        Assert.Equal(
            [("a-diagnostic", 2), ("z-diagnostic", 2)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Equal(
            [
                (LifecyclePoint.BeforeSave, 2),
                (LifecyclePoint.AfterSave, 1),
                (LifecyclePoint.AfterImport, 1),
            ],
            summary.DiagnosticLifecyclePointCounts.Select(static count => (count.LifecyclePoint, count.Count)).ToList());
        Assert.Equal(
            [("AHook", 2), ("ZHook", 2)],
            summary.DiagnosticHookTypeCounts.Select(static count => (count.HookType, count.Count)).ToList());
    }

    [Fact]
    public void Summary_BlankOutcomeAndDiagnosticKeys_AreIgnoredInKeyCounts()
    {
        var summary = new[]
        {
            LifecycleHookOutcome.Reject(
                "",
                "Rejected.",
                [
                    Diagnostic("", LifecyclePoint.BeforeSave, ""),
                    Diagnostic(" ", LifecyclePoint.BeforeSave, " "),
                ]),
            LifecycleHookOutcome.Fail(" ", "Failed."),
            LifecycleHookOutcome.Fail("failed", "Failed."),
        }.ToSummary();

        Assert.Equal(3, summary.TotalOutcomeCount);
        Assert.Equal(2, summary.TotalDiagnosticCount);
        Assert.Equal(
            [("failed", 1)],
            summary.OutcomeCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Empty(summary.DiagnosticCodeCounts);
        Assert.Empty(summary.DiagnosticHookTypeCounts);
        Assert.Equal(
            [(LifecyclePoint.BeforeSave, 2)],
            summary.DiagnosticLifecyclePointCounts.Select(static count => (count.LifecyclePoint, count.Count)).ToList());
    }

    [Fact]
    public void Summary_NullNestedDiagnosticsAndNullEnumerableEntries_AreIgnored()
    {
        var summary = new[]
        {
            LifecycleHookOutcome.Reject("rejected", "Rejected.") with { Diagnostics = null! },
            null!,
            LifecycleHookOutcome.Continue(),
        }.ToSummary();

        Assert.Equal(2, summary.TotalOutcomeCount);
        Assert.Equal(0, summary.TotalDiagnosticCount);
        Assert.Empty(summary.DiagnosticCodeCounts);
        Assert.Equal(
            [
                (LifecycleHookOutcomeStatus.Continue, 1),
                (LifecycleHookOutcomeStatus.Rejected, 1),
            ],
            summary.StatusCounts.Select(static count => (count.Status, count.Count)).ToList());
    }

    private static LifecycleHookDiagnostic Diagnostic(
        string code,
        LifecyclePoint lifecyclePoint,
        string? hookType) =>
        new()
        {
            Code = code,
            Message = code,
            LifecyclePoint = lifecyclePoint,
            HookType = hookType,
        };
}
