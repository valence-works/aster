using System.Text.Json;
using System.Text.Json.Serialization;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;

namespace Aster.Studio;

internal sealed record FacetDto(
    string FacetId,
    string Type,
    bool IsRequired,
    FacetMetadataDto? Metadata);

internal sealed record FacetMetadataDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DisplayOrder { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? DefaultValue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<object?>? AllowedValues { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinLength { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? MinValue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? MaxValue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pattern { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinItems { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxItems { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UniqueItems { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UiHint { get; init; }
}

internal sealed record DiagnosticDto(
    string Code,
    string? AspectKey,
    string? FacetId,
    string Message);

internal sealed record DiagnosticsEnvelope(IReadOnlyList<DiagnosticDto> Diagnostics);

internal sealed record ErrorEnvelope(string Error);

internal sealed record AspectUsageDto(
    string DefinitionId,
    int DefinitionVersion,
    string AttachmentKey,
    int PinnedVersion);

internal sealed record SharedAspectDto(
    string AspectId,
    string DisplayName,
    int Version,
    bool RequiresName,
    IReadOnlyList<FacetDto> Facets,
    IReadOnlyList<int> Versions,
    IReadOnlyList<AspectUsageDto> UsedBy);

internal sealed record AspectFacetChangeDto(
    string FacetId,
    string Kind,
    string? From,
    string? To);

internal sealed record AspectUpgradeCandidateDto(
    string DefinitionId,
    int DefinitionVersion,
    string AttachmentKey,
    int PinnedVersion,
    IReadOnlyList<AspectFacetChangeDto> Changes);

internal sealed record AspectUpgradePreviewDto(
    string AspectId,
    int? TargetVersion,
    IReadOnlyList<AspectUpgradeCandidateDto> Candidates,
    IReadOnlyList<DiagnosticDto> Diagnostics);

internal sealed record AspectUpgradeRequestDto(
    int? TargetVersion,
    IReadOnlyList<string> DefinitionIds);

internal sealed record AspectUpgradeOutcomeDto(
    string DefinitionId,
    string Status,
    int? NewDefinitionVersion,
    IReadOnlyList<string> AttachmentKeys,
    IReadOnlyList<DiagnosticDto> Diagnostics);

internal sealed record AspectUpgradeResultDto(
    int? TargetVersion,
    IReadOnlyList<AspectUpgradeOutcomeDto> Outcomes,
    IReadOnlyList<DiagnosticDto> Diagnostics);

internal sealed record DefinitionSummaryDto(
    string DefinitionId,
    int Version,
    bool IsSingleton,
    string DisplayName,
    int AspectCount,
    int ResourceCount);

internal sealed record ResourceListResponse(
    IReadOnlyList<ResourceListItemDto> Items,
    int TotalCount);

internal sealed record ResourceListItemDto(
    string ResourceId,
    string DefinitionId,
    int Version,
    DateTime Created,
    string Title,
    IReadOnlyList<string> ActiveChannels,
    IReadOnlyList<string> Tags,
    ResourceLifecycleMarkerState LifecycleState);

internal sealed record LifecycleStateDto(
    string ResourceId,
    ResourceLifecycleMarkerState State,
    DateTimeOffset? MarkedAt,
    string? Reason);

internal sealed record LifecycleMarkerRequestDto(
    ResourceLifecycleMarkerState State,
    string? Reason);

internal sealed record LifecycleRestoreRequestDto(ResourceLifecycleMarkerState ExpectedState);

/// <summary>A summary from the SDK's summary helpers with the per-candidate results it summarizes.</summary>
internal sealed record OperationResultDto<TSummary, TCandidate>(
    TSummary Summary,
    IReadOnlyList<TCandidate> Candidates);

internal sealed record SchemaStatusDto(
    string ResourceId,
    int ResourceVersion,
    string DefinitionId,
    int? RecordedDefinitionVersion,
    int? LatestDefinitionVersion,
    ResourceSchemaStatus Status,
    string Message,
    SchemaUpgradePreviewDto Upgrade);

/// <summary>What upgrading the latest version to the latest definition would do, from the SDK's upgrade preview.</summary>
internal sealed record SchemaUpgradePreviewDto(
    ResourceSchemaUpgradeStatus Status,
    int? TargetDefinitionVersion,
    IReadOnlyList<string> CarriedForwardAspectKeys,
    IReadOnlyList<DiagnosticDto> Diagnostics);

internal sealed record SchemaUpgradeRequestDto(int BaseVersion);

internal sealed record SchemaUpgradeResultDto(
    ResourceSchemaUpgradeStatus Status,
    int? ResourceVersion,
    int? SourceDefinitionVersion,
    int TargetDefinitionVersion,
    IReadOnlyList<string> CarriedForwardAspectKeys,
    string Message);

internal sealed record DefinitionPoliciesDto(
    string DefinitionId,
    int DefinitionVersion,
    IReadOnlyList<ResourcePolicyDeclaration> Policies,
    IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics);

internal sealed record PolicyPreviewRequestDto(DateTimeOffset? EvaluationTimestamp);

/// <summary>
/// A policy preview candidate. Pruning previews leave out versions pruning protects and report them as diagnostics.
/// </summary>
internal sealed record PolicyPreviewCandidateDto(
    string PolicyId,
    ResourcePolicyKind PolicyKind,
    ResourcePolicyOutcome Outcome,
    string ResourceId,
    int? ResourceVersion,
    string Reason);

internal sealed record PolicyPreviewDto(
    ResourcePolicyPreviewSummary Summary,
    IReadOnlyList<PolicyPreviewCandidateDto> Candidates,
    IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics);

internal sealed record PolicyApplicationRequestDto(
    IReadOnlyList<ResourcePolicyApplicationCandidate> Candidates,
    string? Reason);

internal sealed record PolicyPruningRequestDto(IReadOnlyList<ResourcePolicyPruningApplicationCandidate> Candidates);

internal sealed record TenantDto(string TenantId, string DisplayName);

/// <summary>A portability result with the summary the SDK's summary helpers make of it.</summary>
internal sealed record PortabilityResultDto<TSummary, TResult>(TSummary Summary, TResult Result);

internal sealed record SnapshotImportRequestDto(PortableSnapshot? Snapshot, PortableImportCollisionMode CollisionMode);

/// <summary>
/// The query builder's query: conditions combined by <paramref name="Match"/>, so condition <c>i</c> is the filter's
/// operand <c>i</c> and validation paths such as <c>Filter.Operands[1].Value</c> point at a condition.
/// </summary>
internal sealed record QueryRequestDto(
    string? DefinitionId,
    ResourceVersionScope Scope,
    string? Channel,
    string? Lifecycle,
    LogicalOperator Match,
    IReadOnlyList<QueryConditionDto>? Conditions,
    IReadOnlyList<QuerySortDto>? Sort,
    int? Take);

/// <summary>
/// One condition. <paramref name="Kind"/> is <c>metadata</c> (<paramref name="Field"/>), <c>facet</c>
/// (<paramref name="AspectKey"/> and <paramref name="FacetId"/>), <c>exists</c> (<paramref name="AspectKey"/>) or
/// <c>references</c> (a reference facet naming the resource ID in <paramref name="Value"/>). A range value is an
/// object with <c>min</c> and <c>max</c>.
/// </summary>
internal sealed record QueryConditionDto(
    string Kind,
    string? Field,
    string? AspectKey,
    string? FacetId,
    ComparisonOperator Operator,
    JsonElement? Value);

internal sealed record QuerySortDto(string Field, string? AspectKey, bool Descending);

internal sealed record QueryValidationDto(QueryValidationSummary Summary, IReadOnlyList<QueryValidationFailure> Failures);

internal sealed record QueryResultItemDto(string ResourceId, string DefinitionId, int Version, DateTime Created, string Title);

internal sealed record QueryResultDto(IReadOnlyList<QueryResultItemDto> Items, bool HasMore);
