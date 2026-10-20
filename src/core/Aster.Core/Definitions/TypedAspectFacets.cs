using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Aster.Core.Models.Definitions;

namespace Aster.Core.Definitions;

/// <summary>
/// Derives facet definitions from the public properties of a typed (POCO) aspect, following
/// the CLR-to-facet type mapping in ADR 0002 §8.
/// </summary>
/// <remarks>
/// Only facet types that registration currently accepts are derived. Properties whose type maps
/// to a type that is not supported yet (object) are skipped, so they stay unvalidated exactly
/// as before; they are picked up when that type lands (#84). Collections derive <c>T[]</c> when
/// their element type is a supported non-nullable scalar; collections of anything else (records,
/// enums, <c>Nullable&lt;T&gt;</c>, whose null elements an array facet would reject) are skipped,
/// as is <c>byte[]</c>, which System.Text.Json writes as a base64 string rather than an array.
/// Enum properties are skipped too: the stores serialize enums as integers, and the facet
/// representation for enums is decided separately (#131).
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
        var type = Map(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
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
            : MapScalar(clrType);

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
