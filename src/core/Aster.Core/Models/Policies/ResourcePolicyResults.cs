using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Policies;

/// <summary>
/// Validation result for a set of policy declarations.
/// </summary>
public sealed record ResourcePolicyValidationResult
{
    /// <summary>Successful validation result.</summary>
    public static ResourcePolicyValidationResult Success { get; } = new();

    /// <summary>Whether validation found no errors.</summary>
    public bool IsValid => Diagnostics.Count == 0;

    /// <summary>Stable policy diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Request for deterministic policy preview evaluation.
/// </summary>
public sealed class ResourcePolicyEvaluationRequest
{
    /// <summary>Tenant scope for the preview. When omitted, the default single-tenant scope is used.</summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>Optional bounded definition set.</summary>
    public HashSet<string> DefinitionIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Optional bounded policy set.</summary>
    public HashSet<string> PolicyIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Required timestamp used for age-based criteria.</summary>
    public DateTimeOffset? EvaluationTimestamp { get; set; }
}

/// <summary>
/// Non-mutating policy preview result.
/// </summary>
public sealed record ResourcePolicyEvaluationPreview
{
    /// <summary>Effective tenant that was evaluated.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Timestamp used for age calculations, when supplied.</summary>
    public DateTimeOffset? EvaluationTimestamp { get; init; }

    /// <summary>Candidate policy outcomes.</summary>
    public IReadOnlyList<ResourcePolicyCandidateOutcome> Candidates { get; init; } = [];

    /// <summary>Validation and preview diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Resource or version that matched a policy declaration during preview.
/// </summary>
public sealed record ResourcePolicyCandidateOutcome
{
    /// <summary>Policy declaration identifier.</summary>
    public required string PolicyId { get; init; }

    /// <summary>Policy kind.</summary>
    public required ResourcePolicyKind PolicyKind { get; init; }

    /// <summary>Previewed outcome.</summary>
    public required ResourcePolicyOutcome Outcome { get; init; }

    /// <summary>Affected logical resource.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Affected version when the outcome is version-specific.</summary>
    public int? ResourceVersion { get; init; }

    /// <summary>Host-visible reason for the candidate.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Stable policy diagnostic.
/// </summary>
public sealed record ResourcePolicyDiagnostic
{
    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable diagnostic message.</summary>
    public required string Message { get; init; }

    /// <summary>Path to the invalid or unsupported value when available.</summary>
    public string? Path { get; init; }

    /// <summary>Policy identifier when available.</summary>
    public string? PolicyId { get; init; }

    /// <summary>Resource identifier when available.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Resource version when available.</summary>
    public int? ResourceVersion { get; init; }
}

/// <summary>
/// Stable policy diagnostic codes.
/// </summary>
public static class ResourcePolicyDiagnosticCodes
{
    /// <summary>Policy metadata is invalid.</summary>
    public const string PolicyInvalid = "policy-invalid";

    /// <summary>Policy kind is unsupported.</summary>
    public const string PolicyKindUnsupported = "policy-kind-unsupported";

    /// <summary>Policy outcome is unsupported.</summary>
    public const string PolicyOutcomeUnsupported = "policy-outcome-unsupported";

    /// <summary>Policy target is invalid.</summary>
    public const string PolicyTargetInvalid = "policy-target-invalid";

    /// <summary>Policy criteria are unsupported.</summary>
    public const string PolicyCriteriaUnsupported = "policy-criteria-unsupported";

    /// <summary>Policy declarations conflict.</summary>
    public const string PolicyConflict = "policy-conflict";

    /// <summary>Age-based evaluation needs an explicit timestamp.</summary>
    public const string PolicyEvaluationTimestampRequired = "policy-evaluation-timestamp-required";

    /// <summary>Pruning preview would remove all versions.</summary>
    public const string PolicyPruningUnsafe = "policy-pruning-unsafe";

    /// <summary>Pruning writes are not supported by this slice.</summary>
    public const string PolicyPruningPreviewOnly = "policy-pruning-preview-only";

    /// <summary>Policy pruning application candidate shape is invalid.</summary>
    public const string PolicyPruningCandidateInvalid = "policy-pruning-candidate-invalid";

    /// <summary>Policy pruning application target resource was not found.</summary>
    public const string PolicyPruningTargetNotFound = "policy-pruning-target-not-found";

    /// <summary>Policy pruning application target is the latest version.</summary>
    public const string PolicyPruningVersionProtectedLatest = "policy-pruning-version-protected-latest";

    /// <summary>Policy pruning application target is active in at least one channel.</summary>
    public const string PolicyPruningVersionProtectedActive = "policy-pruning-version-protected-active";

    /// <summary>Policy pruning application candidate references a missing current policy declaration.</summary>
    public const string PolicyPruningPolicyMissing = "policy-pruning-policy-missing";

    /// <summary>Policy pruning application candidate no longer matches current policy criteria.</summary>
    public const string PolicyPruningPolicyMismatch = "policy-pruning-policy-mismatch";

    /// <summary>Active provider does not support destructive resource version pruning.</summary>
    public const string PolicyPruningProviderUnsupported = "policy-pruning-provider-unsupported";

    /// <summary>Provider failed to remove a candidate version after preflight succeeded.</summary>
    public const string PolicyPruningWriteFailed = "policy-pruning-write-failed";

    /// <summary>Policy application candidate shape is invalid.</summary>
    public const string PolicyApplicationCandidateInvalid = "policy-application-candidate-invalid";

    /// <summary>Policy application outcome is unsupported for writes.</summary>
    public const string PolicyApplicationOutcomeUnsupported = "policy-application-outcome-unsupported";

    /// <summary>Policy application candidate refers to a no-longer-latest resource version.</summary>
    public const string PolicyApplicationStaleCandidate = "policy-application-stale-candidate";

    /// <summary>Policy application candidate references a missing current policy declaration.</summary>
    public const string PolicyApplicationPolicyMissing = "policy-application-policy-missing";

    /// <summary>Policy application candidate no longer matches the current policy declaration outcome.</summary>
    public const string PolicyApplicationPolicyMismatch = "policy-application-policy-mismatch";

    /// <summary>Policy application request includes conflicting lifecycle outcomes for the same resource.</summary>
    public const string PolicyApplicationConflictingOutcome = "policy-application-conflicting-outcome";

    /// <summary>Lifecycle marker write conflicts with current marker state.</summary>
    public const string LifecycleMarkerConflict = "lifecycle-marker-conflict";

    /// <summary>Lifecycle marker target resource was not found.</summary>
    public const string LifecycleMarkerTargetNotFound = "lifecycle-marker-target-not-found";

    /// <summary>Lifecycle restore candidate shape is invalid.</summary>
    public const string LifecycleRestoreCandidateInvalid = "lifecycle-restore-candidate-invalid";

    /// <summary>Lifecycle restore expected state is unsupported.</summary>
    public const string LifecycleRestoreStateUnsupported = "lifecycle-restore-state-unsupported";

    /// <summary>Lifecycle restore candidate expected state differs from current marker state.</summary>
    public const string LifecycleRestoreMarkerMismatch = "lifecycle-restore-marker-mismatch";
}
