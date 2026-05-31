using Aster.Core.Models.Policies;

namespace Aster.Tests.Policies;

public sealed class PolicyValidationSummaryTests
{
    [Fact]
    public void Summary_SuccessResult_ReturnsValidZeroCounts()
    {
        var summary = ResourcePolicyValidationResult.Success.ToSummary();

        Assert.True(summary.IsValid);
        Assert.False(summary.HasDiagnostics);
        Assert.Equal(0, summary.TotalDiagnosticCount);
        Assert.Empty(summary.DiagnosticCodeCounts);
        Assert.Empty(summary.DiagnosticPathCounts);
        Assert.Empty(summary.PolicyIdCounts);
        Assert.Empty(summary.ResourceIdCounts);
        Assert.Empty(summary.ResourceVersionCounts);
    }

    [Fact]
    public void Summary_MixedDiagnostics_AggregatesCountsDeterministically()
    {
        var summary = new ResourcePolicyValidationResult
        {
            Diagnostics =
            [
                Diagnostic("z-code", "policyDeclarations/1", "policy-b", "resource-b", 2),
                Diagnostic("a-code", "policyDeclarations/0", "policy-a", "resource-a", 1),
                Diagnostic("z-code", "policyDeclarations/1", "policy-a", "resource-a", 1),
            ],
        }.ToSummary();

        Assert.False(summary.IsValid);
        Assert.True(summary.HasDiagnostics);
        Assert.Equal(3, summary.TotalDiagnosticCount);
        Assert.Equal(
            [("a-code", 1), ("z-code", 2)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Equal(
            [("policyDeclarations/0", 1), ("policyDeclarations/1", 2)],
            summary.DiagnosticPathCounts.Select(static count => (count.Path, count.Count)).ToList());
        Assert.Equal(
            [("policy-a", 2), ("policy-b", 1)],
            summary.PolicyIdCounts.Select(static count => (count.PolicyId, count.Count)).ToList());
        Assert.Equal(
            [("resource-a", 2), ("resource-b", 1)],
            summary.ResourceIdCounts.Select(static count => (count.ResourceId, count.Count)).ToList());
        Assert.Equal(
            [(1, 2), (2, 1)],
            summary.ResourceVersionCounts.Select(static count => (count.ResourceVersion, count.Count)).ToList());
    }

    [Fact]
    public void Summary_BlankStringKeys_AreIgnoredInKeyCounts()
    {
        var summary = new ResourcePolicyValidationResult
        {
            Diagnostics =
            [
                Diagnostic("", "", "", "", null),
                Diagnostic(" ", " ", " ", " ", null),
                Diagnostic(ResourcePolicyDiagnosticCodes.PolicyInvalid, null, null, null, null),
            ],
        }.ToSummary();

        Assert.Equal(3, summary.TotalDiagnosticCount);
        Assert.Equal(
            [(ResourcePolicyDiagnosticCodes.PolicyInvalid, 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Empty(summary.DiagnosticPathCounts);
        Assert.Empty(summary.PolicyIdCounts);
        Assert.Empty(summary.ResourceIdCounts);
        Assert.Empty(summary.ResourceVersionCounts);
    }

    [Fact]
    public void Summary_ResourceVersionCounts_IgnoreMissingVersionsAndSortAscending()
    {
        var summary = new ResourcePolicyValidationResult
        {
            Diagnostics =
            [
                Diagnostic("code", resourceVersion: 10),
                Diagnostic("code"),
                Diagnostic("code", resourceVersion: 2),
                Diagnostic("code", resourceVersion: 10),
            ],
        }.ToSummary();

        Assert.Equal(
            [(2, 1), (10, 2)],
            summary.ResourceVersionCounts.Select(static count => (count.ResourceVersion, count.Count)).ToList());
    }

    [Fact]
    public void Summary_NullDiagnosticsCollection_IsTreatedAsEmpty()
    {
        var summary = new ResourcePolicyValidationResult
        {
            Diagnostics = null!,
        }.ToSummary();

        Assert.True(summary.IsValid);
        Assert.False(summary.HasDiagnostics);
        Assert.Equal(0, summary.TotalDiagnosticCount);
        Assert.Empty(summary.DiagnosticCodeCounts);
    }

    [Fact]
    public void Summary_NullResultThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ((ResourcePolicyValidationResult)null!).ToSummary());
    }

    [Fact]
    public void Summary_IsGeneratedFromManuallyConstructedResultWithoutServices()
    {
        var summary = new ResourcePolicyValidationResult
        {
            Diagnostics =
            [
                Diagnostic(ResourcePolicyDiagnosticCodes.PolicyConflict, policyId: "manual-policy"),
            ],
        }.ToSummary();

        Assert.Equal(1, summary.TotalDiagnosticCount);
        Assert.Equal(
            [("manual-policy", 1)],
            summary.PolicyIdCounts.Select(static count => (count.PolicyId, count.Count)).ToList());
    }

    private static ResourcePolicyDiagnostic Diagnostic(
        string code,
        string? path = null,
        string? policyId = null,
        string? resourceId = null,
        int? resourceVersion = null) =>
        new()
        {
            Code = code,
            Message = code,
            Path = path,
            PolicyId = policyId,
            ResourceId = resourceId,
            ResourceVersion = resourceVersion,
        };
}
