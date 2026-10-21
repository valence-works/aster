using System.Text.Json;
using Aster.Core.Models.Definitions;

namespace Aster.Studio;

internal static class DemoDefinitions
{
    private static readonly JsonSerializerOptions ShapeOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds an aspect definition from Studio facet rows; the store assigns the version.
    /// </summary>
    public static AspectDefinition BuildAspect(string aspectId, bool requiresName, IReadOnlyList<FacetDto> facets) => new()
    {
        AspectDefinitionId = aspectId,
        Id = Guid.NewGuid().ToString("N"),
        Version = 0,
        RequiresName = requiresName,
        FacetDefinitions = facets.Select(ToFacetDefinition).ToList(),
    };

    /// <summary>
    /// Whether two aspect versions declare the same facets, ignoring version-specific IDs. Versions are
    /// append-only, so Studio compares before registering instead of appending an identical version.
    /// </summary>
    public static bool HasSameShape(AspectDefinition left, AspectDefinition right) =>
        left.RequiresName == right.RequiresName
        && string.Equals(ShapeOf(left), ShapeOf(right), StringComparison.Ordinal);

    public static SharedAspectDto ToSharedAspectDto(
        AspectDefinition aspect,
        IReadOnlyList<int> versions,
        IReadOnlyList<AspectUsageDto> usedBy) => new(
        aspect.AspectDefinitionId,
        ToDisplayName(aspect.AspectDefinitionId),
        aspect.Version,
        aspect.RequiresName,
        OrderFacets(aspect.FacetDefinitions).Select(ToFacetDto).ToList(),
        versions,
        usedBy);

    private static IEnumerable<FacetDefinition> OrderFacets(IEnumerable<FacetDefinition> facets) => facets
        .OrderBy(static facet => facet.Metadata?.DisplayOrder ?? int.MaxValue)
        .ThenBy(static facet => facet.FacetDefinitionId, StringComparer.Ordinal);

    private static string ShapeOf(AspectDefinition aspect) => JsonSerializer.Serialize(
        aspect.FacetDefinitions.Select(static facet => new { facet.FacetDefinitionId, facet.Type, facet.IsRequired, facet.Metadata }),
        ShapeOptions);

    public static FacetDto ToFacetDto(FacetDefinition facet) => new(
        facet.FacetDefinitionId,
        ToDtoFacetType(facet.Type),
        facet.IsRequired,
        ToMetadataDto(facet.Metadata));

    public static string ToDisplayName(string value)
    {
        if (string.Equals(value, "seo", StringComparison.OrdinalIgnoreCase))
            return "SEO";

        if (string.IsNullOrWhiteSpace(value))
            return value;

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static FacetDefinition ToFacetDefinition(FacetDto facet) => new()
    {
        FacetDefinitionId = facet.FacetId,
        Id = Guid.NewGuid().ToString("N"),
        Version = 1,
        Type = ToSdkFacetType(facet.Type),
        IsRequired = facet.IsRequired,
        Metadata = ToMetadata(facet.Metadata),
    };

    private static FacetMetadata? ToMetadata(FacetMetadataDto? metadata)
    {
        if (metadata is null)
            return null;

        return new FacetMetadata
        {
            Label = metadata.Label,
            Description = metadata.Description,
            DisplayOrder = metadata.DisplayOrder,
            DefaultValue = DemoJsonValues.ToPlain(metadata.DefaultValue),
            AllowedValues = metadata.AllowedValues?.Select(DemoJsonValues.ToPlain).OfType<object>().ToList(),
            MinLength = metadata.MinLength,
            MaxLength = metadata.MaxLength,
            Minimum = metadata.MinValue,
            Maximum = metadata.MaxValue,
            Pattern = metadata.Pattern,
            MinItems = metadata.MinItems,
            MaxItems = metadata.MaxItems,
            UniqueItems = metadata.UniqueItems,
            UiHint = metadata.UiHint,
        };
    }

    private static FacetMetadataDto? ToMetadataDto(FacetMetadata? metadata)
    {
        if (metadata is null)
            return null;

        return new FacetMetadataDto
        {
            Label = metadata.Label,
            Description = metadata.Description,
            DisplayOrder = metadata.DisplayOrder,
            DefaultValue = DemoJsonValues.ToPlain(metadata.DefaultValue),
            AllowedValues = metadata.AllowedValues?.Select(DemoJsonValues.ToPlain).ToList(),
            MinLength = metadata.MinLength,
            MaxLength = metadata.MaxLength,
            MinValue = metadata.Minimum,
            MaxValue = metadata.Maximum,
            Pattern = metadata.Pattern,
            MinItems = metadata.MinItems,
            MaxItems = metadata.MaxItems,
            UniqueItems = metadata.UniqueItems,
            UiHint = metadata.UiHint,
        };
    }

    // Studio's UI types map element-wise for arrays: "number[]" is "decimal[]".
    private static string ToSdkFacetType(string type) => type.EndsWith("[]", StringComparison.Ordinal)
        ? ToSdkScalarType(type[..^2]) + "[]"
        : ToSdkScalarType(type);

    private static string ToSdkScalarType(string type) => type.ToLowerInvariant() switch
    {
        "number" => "decimal",
        "boolean" => "bool",
        "datetime" => "datetime",
        "string" => "string",
        var other => other,
    };

    private static string ToDtoFacetType(string type) => type.EndsWith("[]", StringComparison.Ordinal)
        ? ToDtoScalarType(type[..^2]) + "[]"
        : ToDtoScalarType(type);

    private static string ToDtoScalarType(string type) => type.ToLowerInvariant() switch
    {
        "decimal" or "int" or "int32" or "int64" or "long" => "number",
        "bool" or "boolean" => "boolean",
        "datetime" or "datetimeoffset" => "datetime",
        "string" => "string",
        var other => other,
    };
}
