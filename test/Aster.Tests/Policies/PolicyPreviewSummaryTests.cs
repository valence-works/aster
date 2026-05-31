using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Tests.Policies;

public sealed class PolicyPreviewSummaryTests
{
    [Fact]
    public void PreviewSummary_MixedCandidates_AggregatesCandidatesOutcomesKindsAndTargets()
    {
        var evaluatedAt = DateTimeOffset.UtcNow;
        var preview = new ResourcePolicyEvaluationPreview
        {
            TenantScope = TenantScope.FromTenantId("tenant-a"),
            EvaluationTimestamp = evaluatedAt,
            Candidates =
            [
                Candidate(ResourcePolicyKind.Archival, ResourcePolicyOutcome.Archive, "product-1"),
                Candidate(ResourcePolicyKind.SoftDelete, ResourcePolicyOutcome.SoftDelete, "product-1"),
                Candidate(ResourcePolicyKind.VersionPruning, ResourcePolicyOutcome.PrunePreview, "product-1", 1),
                Candidate(ResourcePolicyKind.VersionPruning, ResourcePolicyOutcome.PrunePreview, "product-1", 1),
                Candidate(ResourcePolicyKind.Retention, ResourcePolicyOutcome.Retain, "product-2", 2),
                Candidate(ResourcePolicyKind.Archival, ResourcePolicyOutcome.Archive, ""),
            ],
        };

        var summary = preview.ToSummary();

        Assert.Equal(preview.TenantScope, summary.TenantScope);
        Assert.Equal(evaluatedAt, summary.EvaluationTimestamp);
        Assert.Equal(6, summary.TotalCandidateCount);
        Assert.Equal(2, summary.DistinctResourceCount);
        Assert.Equal(2, summary.DistinctResourceVersionTargetCount);
        Assert.Equal(
            [
                (ResourcePolicyOutcome.Retain, 1),
                (ResourcePolicyOutcome.Archive, 2),
                (ResourcePolicyOutcome.SoftDelete, 1),
                (ResourcePolicyOutcome.PrunePreview, 2),
            ],
            summary.OutcomeCounts.Select(static count => (count.Outcome, count.Count)).ToList());
        Assert.Equal(
            [
                (ResourcePolicyKind.Retention, 1),
                (ResourcePolicyKind.Archival, 2),
                (ResourcePolicyKind.SoftDelete, 1),
                (ResourcePolicyKind.VersionPruning, 2),
            ],
            summary.KindCounts.Select(static count => (count.Kind, count.Count)).ToList());
        Assert.False(summary.HasDiagnostics);
        Assert.True(summary.IsDiagnosticFree);
    }

    [Fact]
    public void PreviewSummary_Diagnostics_AggregatesCodesAndIgnoresBlankCodes()
    {
        var preview = new ResourcePolicyEvaluationPreview
        {
            Diagnostics =
            [
                Diagnostic("z-code"),
                Diagnostic(""),
                Diagnostic(" "),
                Diagnostic("a-code"),
                Diagnostic("z-code"),
            ],
        };

        var summary = preview.ToSummary();

        Assert.Equal(0, summary.TotalCandidateCount);
        Assert.True(summary.HasDiagnostics);
        Assert.False(summary.IsDiagnosticFree);
        Assert.Equal(
            [("a-code", 1), ("z-code", 2)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void PreviewSummary_NullInputThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ((ResourcePolicyEvaluationPreview)null!).ToSummary());
    }

    [Fact]
    public void PreviewSummary_NullCollectionsAreTreatedAsEmpty()
    {
        var summary = new ResourcePolicyEvaluationPreview
        {
            Candidates = null!,
            Diagnostics = null!,
        }.ToSummary();

        Assert.Equal(0, summary.TotalCandidateCount);
        Assert.Equal(0, summary.DistinctResourceCount);
        Assert.Equal(0, summary.DistinctResourceVersionTargetCount);
        Assert.Empty(summary.OutcomeCounts);
        Assert.Empty(summary.KindCounts);
        Assert.Empty(summary.DiagnosticCodeCounts);
        Assert.True(summary.IsDiagnosticFree);
    }

    [Fact]
    public void PreviewSummary_IsGeneratedFromManuallyConstructedResultWithoutServices()
    {
        var summary = new ResourcePolicyEvaluationPreview
        {
            Candidates =
            [
                Candidate(ResourcePolicyKind.Archival, ResourcePolicyOutcome.Archive, "manual"),
            ],
        }.ToSummary();

        Assert.Equal(1, summary.TotalCandidateCount);
        Assert.Equal(1, summary.DistinctResourceCount);
        Assert.True(summary.IsDiagnosticFree);
    }

    private static ResourcePolicyCandidateOutcome Candidate(
        ResourcePolicyKind kind,
        ResourcePolicyOutcome outcome,
        string resourceId,
        int? resourceVersion = null) =>
        new()
        {
            PolicyId = $"{kind}-{outcome}",
            PolicyKind = kind,
            Outcome = outcome,
            ResourceId = resourceId,
            ResourceVersion = resourceVersion,
            Reason = "Matched test policy.",
        };

    private static ResourcePolicyDiagnostic Diagnostic(string code) =>
        new()
        {
            Code = code,
            Message = code,
        };
}
