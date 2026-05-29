using Aster.Core.Models.Policies;

namespace Aster.Tests.Policies;

public sealed class PolicyApplicationSummaryTests
{
    [Fact]
    public void ApplicationSummary_MixedCandidates_AggregatesCountsAndDiagnostics()
    {
        var result = new ResourcePolicyApplicationResult
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates =
            [
                ApplicationCandidate(ResourcePolicyApplicationCandidateStatus.Applied, "product-1"),
                ApplicationCandidate(ResourcePolicyApplicationCandidateStatus.AlreadySatisfied, "product-1"),
                ApplicationCandidate(ResourcePolicyApplicationCandidateStatus.Skipped, "product-2", Diagnostic("duplicate")),
                ApplicationCandidate(ResourcePolicyApplicationCandidateStatus.Failed, "product-3", Diagnostic("policy-mismatch"), Diagnostic("duplicate")),
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(1, summary.AppliedCount);
        Assert.Equal(1, summary.AlreadySatisfiedCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.True(summary.HasFailures);
        Assert.False(summary.IsFullySuccessful);
        Assert.Equal(1, summary.AffectedResourceCount);
        Assert.Equal(
            [("duplicate", 2), ("policy-mismatch", 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void ApplicationSummary_EmptyResult_IsFullySuccessful()
    {
        var summary = new ResourcePolicyApplicationResult
        {
            AppliedAt = DateTimeOffset.UtcNow,
        }.ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.False(summary.HasFailures);
        Assert.True(summary.IsFullySuccessful);
        Assert.Equal(0, summary.AffectedResourceCount);
        Assert.Empty(summary.DiagnosticCodeCounts);
    }

    [Fact]
    public void ApplicationSummary_NullCandidateCollection_IsTreatedAsEmpty()
    {
        var summary = new ResourcePolicyApplicationResult
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = null!,
        }.ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.True(summary.IsFullySuccessful);
    }

    [Fact]
    public void PruningSummary_MixedCandidates_AggregatesCountsTargetsAndDiagnostics()
    {
        var result = new ResourcePolicyPruningApplicationResult
        {
            Candidates =
            [
                PruningCandidate(ResourcePolicyPruningApplicationCandidateStatus.Pruned, "product-1", 1),
                PruningCandidate(ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned, "product-1", 1),
                PruningCandidate(ResourcePolicyPruningApplicationCandidateStatus.Skipped, "product-1", 1, Diagnostic("duplicate")),
                PruningCandidate(ResourcePolicyPruningApplicationCandidateStatus.Failed, "product-1", 2, Diagnostic("protected"), Diagnostic("duplicate")),
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(1, summary.PrunedCount);
        Assert.Equal(1, summary.AlreadyPrunedCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.True(summary.HasFailures);
        Assert.False(summary.IsFullySuccessful);
        Assert.Equal(1, summary.AffectedTargetCount);
        Assert.Equal(
            [("duplicate", 2), ("protected", 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void PruningSummary_EmptyResult_IsFullySuccessful()
    {
        var summary = new ResourcePolicyPruningApplicationResult().ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.False(summary.HasFailures);
        Assert.True(summary.IsFullySuccessful);
        Assert.Equal(0, summary.AffectedTargetCount);
        Assert.Empty(summary.DiagnosticCodeCounts);
    }

    [Fact]
    public void PruningSummary_NullCandidateCollection_IsTreatedAsEmpty()
    {
        var summary = new ResourcePolicyPruningApplicationResult
        {
            Candidates = null!,
        }.ToSummary();

        Assert.Equal(0, summary.TotalCount);
        Assert.True(summary.IsFullySuccessful);
    }

    [Fact]
    public void SummaryDiagnosticCounts_IgnoreBlankCodesAndUseOrdinalOrdering()
    {
        var result = new ResourcePolicyApplicationResult
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates =
            [
                ApplicationCandidate(
                    ResourcePolicyApplicationCandidateStatus.Failed,
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
        var applicationSummary = new ResourcePolicyApplicationResult
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = [ApplicationCandidate(ResourcePolicyApplicationCandidateStatus.Applied, "product-1")],
        }.ToSummary();

        var pruningSummary = new ResourcePolicyPruningApplicationResult
        {
            Candidates = [PruningCandidate(ResourcePolicyPruningApplicationCandidateStatus.Pruned, "product-1", 1)],
        }.ToSummary();

        Assert.True(applicationSummary.IsFullySuccessful);
        Assert.True(pruningSummary.IsFullySuccessful);
    }

    private static ResourcePolicyApplicationCandidateResult ApplicationCandidate(
        ResourcePolicyApplicationCandidateStatus status,
        string resourceId,
        params ResourcePolicyDiagnostic[] diagnostics) =>
        new()
        {
            Index = 0,
            Status = status,
            ResourceId = resourceId,
            Diagnostics = diagnostics,
        };

    private static ResourcePolicyPruningApplicationCandidateResult PruningCandidate(
        ResourcePolicyPruningApplicationCandidateStatus status,
        string resourceId,
        int version,
        params ResourcePolicyDiagnostic[] diagnostics) =>
        new()
        {
            Index = 0,
            Status = status,
            ResourceId = resourceId,
            ResourceVersion = version,
            Diagnostics = diagnostics,
        };

    private static ResourcePolicyDiagnostic Diagnostic(string code) =>
        new()
        {
            Code = code,
            Message = code,
        };
}
