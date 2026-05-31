using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;

namespace Aster.Tests.Policies;

public sealed class LifecycleMarkerResultSummaryTests
{
    [Fact]
    public void Summary_SuccessfulSingleResult_ReturnsMarkerAndSuccessCounts()
    {
        var result = new ResourceLifecycleMarkerResult
        {
            Marker = Marker("product-1", ResourceLifecycleMarkerState.Archived),
        };

        var summary = result.ToSummary();

        Assert.True(summary.IsFullySuccessful);
        Assert.False(summary.HasFailures);
        Assert.False(summary.HasDiagnostics);
        Assert.Equal(1, summary.TotalResultCount);
        Assert.Equal(1, summary.SucceededCount);
        Assert.Equal(0, summary.FailedCount);
        Assert.Equal(1, summary.MarkerPresentCount);
        Assert.Equal(0, summary.MissingMarkerCount);
        Assert.Equal(0, summary.TotalDiagnosticCount);
        Assert.Equal(1, summary.DistinctMarkerResourceCount);
        Assert.Equal(
            [(ResourceLifecycleMarkerState.Archived, 1)],
            summary.MarkerStateCounts.Select(static count => (count.State, count.Count)).ToList());
        Assert.Equal(
            [("product-1", 1)],
            summary.MarkerResourceCounts.Select(static count => (count.ResourceId, count.Count)).ToList());
        Assert.Empty(summary.DiagnosticCodeCounts);
        Assert.Empty(summary.DiagnosticPathCounts);
        Assert.Empty(summary.DiagnosticResourceIdCounts);
    }

    [Fact]
    public void Summary_FailedSingleResult_ReturnsDiagnosticCounts()
    {
        var result = new ResourceLifecycleMarkerResult
        {
            Diagnostics =
            [
                Diagnostic(ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound, "resourceId", "missing"),
            ],
        };

        var summary = result.ToSummary();

        Assert.False(summary.IsFullySuccessful);
        Assert.True(summary.HasFailures);
        Assert.True(summary.HasDiagnostics);
        Assert.Equal(1, summary.TotalResultCount);
        Assert.Equal(0, summary.SucceededCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(0, summary.MarkerPresentCount);
        Assert.Equal(1, summary.MissingMarkerCount);
        Assert.Equal(1, summary.TotalDiagnosticCount);
        Assert.Equal(0, summary.DistinctMarkerResourceCount);
        Assert.Empty(summary.MarkerStateCounts);
        Assert.Empty(summary.MarkerResourceCounts);
        Assert.Equal(
            [(ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound, 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Equal(
            [("resourceId", 1)],
            summary.DiagnosticPathCounts.Select(static count => (count.Path, count.Count)).ToList());
        Assert.Equal(
            [("missing", 1)],
            summary.DiagnosticResourceIdCounts.Select(static count => (count.ResourceId, count.Count)).ToList());
    }

    [Fact]
    public void Summary_NullSingleResult_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((ResourceLifecycleMarkerResult)null!).ToSummary());
    }

    [Fact]
    public void Summary_MixedEnumerable_AggregatesResultsMarkersAndDiagnosticsDeterministically()
    {
        ResourceLifecycleMarkerResult[] results =
        [
            new() { Marker = Marker("product-2", ResourceLifecycleMarkerState.SoftDeleted) },
            new()
            {
                Marker = Marker("product-1", ResourceLifecycleMarkerState.Archived),
                Diagnostics = [Diagnostic(ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict, "state", "product-1")],
            },
            new() { Marker = Marker("product-1", ResourceLifecycleMarkerState.Archived) },
            null!,
        ];

        var summary = results.ToSummary();

        Assert.False(summary.IsFullySuccessful);
        Assert.True(summary.HasFailures);
        Assert.True(summary.HasDiagnostics);
        Assert.Equal(3, summary.TotalResultCount);
        Assert.Equal(2, summary.SucceededCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(3, summary.MarkerPresentCount);
        Assert.Equal(0, summary.MissingMarkerCount);
        Assert.Equal(1, summary.TotalDiagnosticCount);
        Assert.Equal(2, summary.DistinctMarkerResourceCount);
        Assert.Equal(
            [(ResourceLifecycleMarkerState.Archived, 2), (ResourceLifecycleMarkerState.SoftDeleted, 1)],
            summary.MarkerStateCounts.Select(static count => (count.State, count.Count)).ToList());
        Assert.Equal(
            [("product-1", 2), ("product-2", 1)],
            summary.MarkerResourceCounts.Select(static count => (count.ResourceId, count.Count)).ToList());
        Assert.Equal(
            [(ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict, 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Equal(
            [("state", 1)],
            summary.DiagnosticPathCounts.Select(static count => (count.Path, count.Count)).ToList());
        Assert.Equal(
            [("product-1", 1)],
            summary.DiagnosticResourceIdCounts.Select(static count => (count.ResourceId, count.Count)).ToList());
    }

    [Fact]
    public void Summary_NullAndEmptyEnumerable_ReturnZeroCounts()
    {
        var nullSummary = ((IEnumerable<ResourceLifecycleMarkerResult>?)null).ToSummary();
        var emptySummary = Array.Empty<ResourceLifecycleMarkerResult>().ToSummary();

        Assert.True(nullSummary.IsFullySuccessful);
        Assert.Equal(0, nullSummary.TotalResultCount);
        Assert.Empty(nullSummary.MarkerStateCounts);

        Assert.True(emptySummary.IsFullySuccessful);
        Assert.Equal(0, emptySummary.TotalResultCount);
        Assert.Empty(emptySummary.DiagnosticCodeCounts);
    }

    [Fact]
    public void Summary_NullDiagnosticsAndBlankKeys_AreIgnoredInKeyCounts()
    {
        var results = new[]
        {
            new ResourceLifecycleMarkerResult
            {
                Marker = Marker(" ", ResourceLifecycleMarkerState.Archived),
                Diagnostics = null!,
            },
            new ResourceLifecycleMarkerResult
            {
                Diagnostics =
                [
                    Diagnostic("", " ", " "),
                    Diagnostic("code-a", "path-a", "resource-a"),
                ],
            },
        };

        var summary = results.ToSummary();

        Assert.False(summary.IsFullySuccessful);
        Assert.Equal(2, summary.TotalResultCount);
        Assert.Equal(1, summary.MarkerPresentCount);
        Assert.Equal(2, summary.TotalDiagnosticCount);
        Assert.Equal(0, summary.DistinctMarkerResourceCount);
        Assert.Equal(
            [(ResourceLifecycleMarkerState.Archived, 1)],
            summary.MarkerStateCounts.Select(static count => (count.State, count.Count)).ToList());
        Assert.Empty(summary.MarkerResourceCounts);
        Assert.Equal(
            [("code-a", 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Equal(
            [("path-a", 1)],
            summary.DiagnosticPathCounts.Select(static count => (count.Path, count.Count)).ToList());
        Assert.Equal(
            [("resource-a", 1)],
            summary.DiagnosticResourceIdCounts.Select(static count => (count.ResourceId, count.Count)).ToList());
    }

    private static ResourceLifecycleMarker Marker(string resourceId, ResourceLifecycleMarkerState state) =>
        new()
        {
            ResourceId = resourceId,
            State = state,
            MarkedAt = DateTimeOffset.UtcNow,
        };

    private static ResourcePolicyDiagnostic Diagnostic(string code, string? path, string? resourceId) =>
        new()
        {
            Code = code,
            Message = code,
            Path = path,
            ResourceId = resourceId,
        };
}
