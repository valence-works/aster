using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Aster.Core.Models.Definitions;

namespace Aster.Core.Definitions;

/// <summary>
/// Derives facet definitions from the public properties of a typed (POCO) aspect, following
/// the CLR-to-facet type mapping in ADR 0002 §8.
/// </summary>
/// <remarks>
/// Nested classes, records, structs and dictionaries derive <c>object</c>, unless
/// System.Text.Json writes them as a JSON scalar (<see cref="TimeSpan"/>, <see cref="Uri"/>, types
/// with a <see cref="JsonConverterAttribute"/>, <see cref="Int128"/>, and similar) or the property's JSON shape is
/// unknown from its type (<see cref="object"/>, <see cref="JsonElement"/>, <see cref="JsonNode"/> other than
/// <see cref="JsonObject"/>, or a property carrying its own <see cref="JsonConverterAttribute"/>); those are skipped. Collections derive <c>T[]</c> when
/// their element type is a supported non-nullable scalar; collections of anything else (records,
/// enums, <c>Nullable&lt;T&gt;</c>, whose null elements an array facet would reject) are skipped,
/// as is <c>byte[]</c>, which System.Text.Json writes as a base64 string rather than an array.
/// Enum properties are skipped too: the stores serialize enums as integers, and the facet
/// representation for enums is decided separately (#131). References are never inferred: a property
/// marked <see cref="ReferenceFacetAttribute"/> derives <c>reference</c> or <c>reference[]</c> instead of
/// <c>string</c> or <c>string[]</c>, and any other shape throws.
/// </remarks>
internal static class TypedAspectFacets
{
    public static IReadOnlyList<FacetDefinition> Derive(Type aspectType) =>
        aspectType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetMethod is not null
                && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
            .OrderBy(static property => property.MetadataToken)
            .Select(ToFacet)
            .OfType<FacetDefinition>()
            .ToList();

    private static FacetDefinition? ToFacet(PropertyInfo property)
    {
        var reference = property.GetCustomAttribute<ReferenceFacetAttribute>(inherit: true);

        // A converter on the property decides its JSON shape, which the CLR type cannot tell.
        var type = property.IsDefined(typeof(JsonConverterAttribute), inherit: true)
            ? null
            : Map(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);

        FacetMetadata? metadata = null;
        if (reference is not null)
        {
            // An explicit opt-in (ADR 0002 §8): the same facet WithFacet(id, "reference") and WithFacetMetadata declare.
            type = type switch
            {
                "string" => "reference",
                "string[]" => "reference[]",
                _ => throw new InvalidOperationException(
                    $"Property '{property.ReflectedType?.Name}.{property.Name}' is marked [ReferenceFacet], but reference facets "
                    + $"derive only from a string or a collection of strings without a [JsonConverter]; its type is '{property.PropertyType}'."),
            };

            if (reference.TargetDefinitionIds.Count > 0 || reference.ReferenceResolution is not null)
            {
                metadata = new FacetMetadata
                {
                    TargetDefinitionIds = reference.TargetDefinitionIds.Count > 0 ? [.. reference.TargetDefinitionIds] : null,
                    ReferenceResolution = reference.ReferenceResolution,
                };
            }
        }

        if (type is null)
            return null;

        return new FacetDefinition
        {
            // The serialized name, so validation and queries over stored JSON find the value.
            FacetDefinitionId = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name,
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            Type = type,
            IsRequired = property.IsDefined(typeof(RequiredMemberAttribute), inherit: false),
            Metadata = metadata,
        };
    }

    private static readonly HashSet<Type> CollectionDefinitions =
    [
        typeof(IEnumerable<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>),
        typeof(ICollection<>), typeof(IList<>), typeof(List<>),
        typeof(ISet<>), typeof(IReadOnlySet<>), typeof(HashSet<>),
    ];

    private static string? Map(Type clrType) =>
        ElementTypeOf(clrType) is { } elementType
            ? MapScalar(elementType) is { } element ? element + "[]" : null
            : MapScalar(clrType) ?? MapObject(clrType);

    private static string? MapObject(Type clrType)
    {
        if (IsDictionary(clrType))
            return "object";

        // JsonObject always writes an object; other JSON DOM types can hold any JSON kind.
        if (clrType == typeof(JsonObject))
            return "object";

        var writtenAsScalar = clrType == typeof(object)
            || clrType == typeof(JsonElement) || clrType == typeof(JsonDocument)
            || typeof(JsonNode).IsAssignableFrom(clrType)
            || clrType == typeof(Int128) || clrType == typeof(UInt128) || clrType == typeof(Half)
            || clrType.IsEnum
            || clrType.IsPrimitive
            || typeof(System.Collections.IEnumerable).IsAssignableFrom(clrType)
            || clrType == typeof(TimeSpan) || clrType == typeof(DateOnly) || clrType == typeof(TimeOnly)
            || clrType == typeof(Uri) || clrType == typeof(Version)
            || clrType.IsDefined(typeof(JsonConverterAttribute), inherit: true);

        return writtenAsScalar ? null : "object";
    }

    private static bool IsDictionary(Type clrType) =>
        typeof(System.Collections.IDictionary).IsAssignableFrom(clrType)
        || (clrType.IsInterface ? [clrType, .. clrType.GetInterfaces()] : clrType.GetInterfaces())
            .Any(static contract => contract.IsGenericType
                && (contract.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || contract.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

    private static Type? ElementTypeOf(Type clrType) =>
        clrType == typeof(byte[]) ? null
        : clrType.IsArray ? (clrType.GetArrayRank() == 1 ? clrType.GetElementType() : null)
        : clrType.IsGenericType && CollectionDefinitions.Contains(clrType.GetGenericTypeDefinition())
            ? clrType.GetGenericArguments()[0]
            : null;

    private static string? MapScalar(Type clrType) =>
        clrType.IsEnum
            ? null
            : Type.GetTypeCode(clrType) switch
            {
                TypeCode.String => "string",
                TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                    or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 => "int",
                TypeCode.Decimal or TypeCode.Double or TypeCode.Single => "decimal",
                TypeCode.Boolean => "bool",
                TypeCode.DateTime => "datetime",
                _ when clrType == typeof(DateTimeOffset) => "datetime",
                _ when clrType == typeof(Guid) => "guid",
                _ => null,
            };
}
