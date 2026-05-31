using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Tests.Lifecycle;

public sealed class ResourceLifecycleRestoreSummaryTests
{
    [Fact]
    public void ApplicationSummary_MixedCandidates_AggregatesCountsResourcesAndDiagnostics()
    {
        var restoredAt = DateTimeOffset.UtcNow;
        var result = new ResourceLifecycleRestoreApplicationResult
        {
            TenantScope = TenantScope.FromTenantId("tenant-a"),
            RestoredAt = restoredAt,
            Candidates =
            [
                Candidate(ResourceLifecycleRestoreCandidateStatus.Restored, "product-1"),
                Candidate(ResourceLifecycleRestoreCandidateStatus.AlreadyRestored, "product-1"),
                Candidate(ResourceLifecycleRestoreCandidateStatus.Skipped, "product-2", Diagnostic("duplicate")),
                Candidate(ResourceLifecycleRestoreCandidateStatus.Failed, "product-3", Diagnostic("marker-mismatch"), Diagnostic("duplicate")),
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(result.TenantScope, summary.TenantScope);
        Assert.Equal(restoredAt, summary.RestoredAt);
        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(1, summary.RestoredCount);
        Assert.Equal(1, summary.AlreadyRestoredCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.True(summary.HasFailures);
        Assert.False(summary.IsFullySuccessful);
        Assert.Equal(1, summary.AffectedResourceCount);
        Assert.Equal(
            [("duplicate", 2), ("marker-mismatch", 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void ApplicationSummary_EmptyResult_IsFullySuccessful()
    {
        var summary = new ResourceLifecycleRestoreApplicationResult().ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.False(summary.HasFailures);
        Assert.True(summary.IsFullySuccessful);
        Assert.Equal(0, summary.AffectedResourceCount);
        Assert.Empty(summary.DiagnosticCodeCounts);
    }

    [Fact]
    public void ApplicationSummary_NullCandidateCollection_IsTreatedAsEmpty()
    {
        var summary = new ResourceLifecycleRestoreApplicationResult
        {
            Candidates = null!,
        }.ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.True(summary.IsFullySuccessful);
    }

    [Fact]
    public void PreviewSummary_MixedCandidates_AggregatesCountsResourcesAndDiagnostics()
    {
        var result = new ResourceLifecycleRestorePreviewResult
        {
            TenantScope = TenantScope.FromTenantId("tenant-a"),
            Candidates =
            [
                Candidate(ResourceLifecycleRestoreCandidateStatus.Restorable, "product-1"),
                Candidate(ResourceLifecycleRestoreCandidateStatus.AlreadyRestored, "product-1"),
                Candidate(ResourceLifecycleRestoreCandidateStatus.Skipped, "product-2", Diagnostic("duplicate")),
                Candidate(ResourceLifecycleRestoreCandidateStatus.Failed, "product-3", Diagnostic("marker-mismatch"), Diagnostic("duplicate")),
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(result.TenantScope, summary.TenantScope);
        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(1, summary.RestorableCount);
        Assert.Equal(1, summary.AlreadyRestoredCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.True(summary.HasFailures);
        Assert.False(summary.IsFullySuccessful);
        Assert.Equal(1, summary.CandidateResourceCount);
        Assert.Equal(
            [("duplicate", 2), ("marker-mismatch", 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void PreviewSummary_EmptyResult_IsFullySuccessful()
    {
        var summary = new ResourceLifecycleRestorePreviewResult().ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.False(summary.HasFailures);
        Assert.True(summary.IsFullySuccessful);
        Assert.Equal(0, summary.CandidateResourceCount);
        Assert.Empty(summary.DiagnosticCodeCounts);
    }

    [Fact]
    public void PreviewSummary_NullCandidateCollection_IsTreatedAsEmpty()
    {
        var summary = new ResourceLifecycleRestorePreviewResult
        {
            Candidates = null!,
        }.ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.True(summary.IsFullySuccessful);
    }

    [Fact]
    public void Summaries_NullInputsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ((ResourceLifecycleRestoreApplicationResult)null!).ToSummary());
        Assert.Throws<ArgumentNullException>(() => ((ResourceLifecycleRestorePreviewResult)null!).ToSummary());
    }

    [Fact]
    public void SummaryDiagnosticCounts_IgnoreBlankCodesAndUseOrdinalOrdering()
    {
        var result = new ResourceLifecycleRestoreApplicationResult
        {
            Candidates =
            [
                Candidate(
                    ResourceLifecycleRestoreCandidateStatus.Failed,
                    "product-1",
                    Diagnostic("z-code"),
                    Diagnostic(""),
                    Diagnostic(" "),
                    Diagnostic("a-code"),
                    Diagnostic("z-code")),
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(
            [("a-code", 1), ("z-code", 2)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void Summaries_AreGeneratedFromManuallyConstructedResultsWithoutServices()
    {
        var applicationSummary = new ResourceLifecycleRestoreApplicationResult
        {
            Candidates = [Candidate(ResourceLifecycleRestoreCandidateStatus.Restored, "product-1")],
        }.ToSummary();
        var previewSummary = new ResourceLifecycleRestorePreviewResult
        {
            Candidates = [Candidate(ResourceLifecycleRestoreCandidateStatus.Restorable, "product-1")],
        }.ToSummary();

        Assert.True(applicationSummary.IsFullySuccessful);
        Assert.True(previewSummary.IsFullySuccessful);
    }

    private static ResourceLifecycleRestoreCandidateResult Candidate(
        ResourceLifecycleRestoreCandidateStatus status,
        string resourceId,
        params ResourcePolicyDiagnostic[] diagnostics) =>
        new()
        {
            Index = 0,
            Status = status,
            ResourceId = resourceId,
            ExpectedState = ResourceLifecycleMarkerState.Archived,
            Diagnostics = diagnostics,
        };

    private static ResourcePolicyDiagnostic Diagnostic(string code) =>
        new()
        {
            Code = code,
            Message = code,
        };
}
