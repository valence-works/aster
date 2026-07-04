using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;

namespace Aster.AdminDemo;

internal static class DemoDefinitions
{
    public static ResourceDefinition BuildDefinition(
        string definitionId,
        bool isSingleton,
        IEnumerable<(string AspectKey, IReadOnlyList<FacetDto> Facets)> aspects)
    {
        var aspectDefinitions = new Dictionary<string, AspectDefinition>(StringComparer.Ordinal);
        foreach (var (aspectKey, facets) in aspects)
        {
            if (!aspectDefinitions.TryAdd(aspectKey, new AspectDefinition
            {
                AspectDefinitionId = aspectKey,
                Id = Guid.NewGuid().ToString("N"),
                Version = 1,
                RequiresName = false,
                FacetDefinitions = facets.Select(ToFacetDefinition).ToList(),
            }))
            {
                throw new InvalidOperationException($"Aspect '{aspectKey}' is duplicated.");
            }
        }

        return new ResourceDefinitionBuilder()
            .WithDefinitionId(definitionId)
            .WithSingleton(isSingleton)
            .Build() with
            {
                AspectDefinitions = aspectDefinitions,
            };
    }

    public static DefinitionDetailDto ToDetailDto(ResourceDefinition definition) => new(
        definition.DefinitionId,
        definition.Version,
        definition.IsSingleton,
        ToDisplayName(definition.DefinitionId),
        definition.AspectDefinitions
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => new AspectDto(
                item.Key,
                ToDisplayName(item.Key),
                item.Value.RequiresName,
                item.Value.FacetDefinitions
                    .OrderBy(static facet => facet.Metadata?.DisplayOrder ?? int.MaxValue)
                    .ThenBy(static facet => facet.FacetDefinitionId, StringComparer.Ordinal)
                    .Select(ToFacetDto)
                    .ToList()))
            .ToList());

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
            UiHint = metadata.UiHint,
        };
    }

    private static string ToSdkFacetType(string type) => type.ToLowerInvariant() switch
    {
        "number" => "decimal",
        "boolean" => "bool",
        "datetime" => "datetime",
        "string" => "string",
        var other => other,
    };

    private static string ToDtoFacetType(string type) => type.ToLowerInvariant() switch
    {
        "decimal" or "int" or "int32" or "int64" or "long" => "number",
        "bool" or "boolean" => "boolean",
        "datetime" or "datetimeoffset" => "datetime",
        "string" => "string",
        var other => other,
    };
}
