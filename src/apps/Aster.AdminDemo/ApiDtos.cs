using System.Text.Json.Serialization;

namespace Aster.AdminDemo;

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

internal sealed record CreateSharedAspectRequest(
    string AspectId,
    bool RequiresName,
    IReadOnlyList<FacetDto> Facets);

internal sealed record UpdateSharedAspectRequest(
    int BaseVersion,
    bool RequiresName,
    IReadOnlyList<FacetDto> Facets);

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
    IReadOnlyList<string> Tags);

