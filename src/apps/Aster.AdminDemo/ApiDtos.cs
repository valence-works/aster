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

internal sealed record AspectDto(
    string AspectKey,
    string DisplayName,
    bool RequiresName,
    IReadOnlyList<FacetDto> Facets);

internal sealed record DiagnosticDto(
    string Code,
    string? AspectKey,
    string? FacetId,
    string Message);

internal sealed record DiagnosticsEnvelope(IReadOnlyList<DiagnosticDto> Diagnostics);

internal sealed record ErrorEnvelope(string Error);

internal sealed record AspectTemplateDto(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<FacetDto> Facets);

internal sealed record DefinitionSummaryDto(
    string DefinitionId,
    int Version,
    bool IsSingleton,
    string DisplayName,
    int AspectCount,
    int ResourceCount);

internal sealed record DefinitionDetailDto(
    string DefinitionId,
    int Version,
    bool IsSingleton,
    string DisplayName,
    IReadOnlyList<AspectDto> Aspects);

internal sealed record CreateDefinitionRequest(
    string DefinitionId,
    bool IsSingleton,
    IReadOnlyList<CreateAspectRequest> Aspects);

internal sealed record CreateAspectRequest(
    string AspectKey,
    string? TemplateKey,
    IReadOnlyList<FacetDto>? Facets);

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

internal sealed record ResourceDetailDto(
    string ResourceId,
    string DefinitionId,
    int Version,
    DateTime Created,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Aspects,
    IReadOnlyDictionary<string, IReadOnlyList<int>> ActiveChannels);

internal sealed record ResourceVersionDto(
    int Version,
    DateTime Created,
    bool IsLatest,
    IReadOnlyList<string> ActiveChannels);

internal sealed record CreateResourceRequestDto(
    string DefinitionId,
    Dictionary<string, Dictionary<string, object?>> Aspects);

internal sealed record UpdateResourceRequestDto(
    int BaseVersion,
    Dictionary<string, Dictionary<string, object?>> Aspects);

internal sealed record ActivationRequestDto(
    int Version,
    string Channel);

internal sealed record StatusDto(string Status);
