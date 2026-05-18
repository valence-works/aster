using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Core.Services;

/// <summary>
/// Evaluates provider-declared index projections against resource version snapshots.
/// </summary>
public sealed class IndexProjectionEvaluator
{
    private readonly IndexProjectionValidator validator = new();

    /// <summary>
    /// Evaluates index projections against a resource version.
    /// </summary>
    /// <param name="resource">The resource version snapshot.</param>
    /// <param name="projections">The projection declarations.</param>
    /// <returns>Successful projection values and structured per-projection failures.</returns>
    public IndexProjectionEvaluationResult Evaluate(Resource resource, IEnumerable<IndexProjection> projections)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(projections);

        var projectionList = projections.ToList();
        var values = new List<IndexProjectionValue>();
        var failures = new List<IndexProjectionFailure>();

        var validation = validator.Validate(projectionList);
        failures.AddRange(validation.Failures);

        var invalidFields = validation.Failures
            .Where(static failure => failure.Code == IndexProjectionFailureCodes.InvalidProjectionDeclaration)
            .Select(failure => failure.FieldName)
            .Where(static fieldName => fieldName is not null)
            .ToHashSet(StringComparer.Ordinal);
        var evaluatedFields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var projection in projectionList)
        {
            if (invalidFields.Contains(projection.FieldName))
                continue;

            if (!evaluatedFields.Add(projection.FieldName))
                continue;

            if (!TryResolveSourceValue(resource, projection.Source, out var sourceValue)
                || sourceValue is null)
            {
                failures.Add(Failure(
                    projection,
                    IndexProjectionFailureCodes.MissingSource,
                    $"Index projection source {projection.Source.Describe()} was not found."));
                continue;
            }

            if (!TryNormalizeValue(projection, sourceValue, out var value))
            {
                failures.Add(Failure(
                    projection,
                    IndexProjectionFailureCodes.IncompatibleValueShape,
                    $"Index projection value from {projection.Source.Describe()} does not match field type '{projection.FieldType}'."));
                continue;
            }

            values.Add(new(projection.FieldName, projection.FieldType, value));
        }

        return IndexProjectionEvaluationResult.Create(values, failures);
    }

    private static bool TryResolveSourceValue(Resource resource, IndexProjectionSource source, out object? value)
    {
        switch (source.Kind)
        {
            case IndexProjectionSourceKind.Metadata:
                return TryResolveMetadataValue(resource, source.MetadataField, out value);
            case IndexProjectionSourceKind.Facet:
                return TryResolveFacetValue(resource, source.AspectKey, source.FacetKey, out value);
            default:
                value = null;
                return false;
        }
    }

    private static bool TryResolveMetadataValue(Resource resource, string? field, out object? value)
    {
        value = field?.ToUpperInvariant() switch
        {
            "RESOURCEID" => resource.ResourceId,
            "ID" => resource.Id,
            "DEFINITIONID" => resource.DefinitionId,
            "DEFINITIONVERSION" => resource.DefinitionVersion,
            "VERSION" => resource.Version,
            "CREATED" => resource.Created,
            "OWNER" => resource.Owner,
            "HASH" => resource.Hash,
            _ => null,
        };

        return value is not null;
    }

    private static bool TryResolveFacetValue(
        Resource resource,
        string? aspectKey,
        string? facetKey,
        out object? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(aspectKey)
            || string.IsNullOrWhiteSpace(facetKey)
            || !resource.Aspects.TryGetValue(aspectKey, out var aspectRaw))
        {
            return false;
        }

        if (IsDictionary(aspectRaw))
            return TryGetDictionaryValue(aspectRaw, facetKey, out value) && value is not null;

        if (aspectRaw is JsonElement element)
            return TryGetJsonElementValue(element, facetKey, out value) && value is not null;

        var property = aspectRaw.GetType().GetProperty(
            facetKey,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property is null)
            return false;

        value = property.GetValue(aspectRaw);
        return value is not null;
    }

    private static bool IsDictionary(object value) =>
        value is IReadOnlyDictionary<string, object>
            or IDictionary<string, object>
            or IDictionary;

    private static bool TryGetDictionaryValue(object value, string key, out object? result)
    {
        switch (value)
        {
            case IReadOnlyDictionary<string, object> readOnlyDictionary:
                return readOnlyDictionary.TryGetValue(key, out result);
            case IDictionary<string, object> dictionary:
                return dictionary.TryGetValue(key, out result);
            case IDictionary dictionary:
                if (dictionary.Contains(key))
                {
                    result = dictionary[key];
                    return true;
                }

                break;
        }

        result = null;
        return false;
    }

    private static bool TryGetJsonElementValue(JsonElement element, string key, out object? value)
    {
        if (!element.TryGetProperty(key, out var property)
            && !element.TryGetProperty(ToCamelCase(key), out property))
        {
            value = null;
            return false;
        }

        value = ConvertJsonElement(property);
        return value is not null;
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
        _ => null,
    };

    private static bool TryNormalizeValue(IndexProjection projection, object value, out object normalized)
    {
        if (projection.IsMultiValue)
            return TryNormalizeMultiValue(projection, value, out normalized);

        if (value is IEnumerable and not string)
        {
            normalized = string.Empty;
            return false;
        }

        return projection.FieldType switch
        {
            IndexFieldType.Keyword or IndexFieldType.Text or IndexFieldType.NormalizedText
                when value is string text => Success(text, out normalized),
            IndexFieldType.Boolean when value is bool boolean => Success(boolean, out normalized),
            IndexFieldType.Integer when TryNormalizeInteger(value, out var integer) => Success(integer, out normalized),
            IndexFieldType.Decimal when TryNormalizeDecimal(value, out var decimalValue) => Success(decimalValue, out normalized),
            IndexFieldType.DateTime when QueryDateTimeValue.TryNormalizeDateKey(value, out var dateKey) => Success(dateKey, out normalized),
            IndexFieldType.Guid when value is Guid guid => Success(guid, out normalized),
            _ => Failure(out normalized),
        };
    }

    private static bool TryNormalizeMultiValue(IndexProjection projection, object value, out object normalized)
    {
        if (projection.FieldType != IndexFieldType.KeywordArray)
            return Failure(out normalized);

        if (value is JsonElement { ValueKind: JsonValueKind.Array } jsonArray)
        {
            var values = jsonArray
                .EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .ToList();
            if (values.Any(static item => item is null))
                return Failure(out normalized);

            return Success(values.Select(static item => item!).ToArray(), out normalized);
        }

        if (value is not IEnumerable enumerable || value is string)
            return Failure(out normalized);

        var strings = new List<string>();
        foreach (var item in enumerable)
        {
            if (item is not string text)
                return Failure(out normalized);

            strings.Add(text);
        }

        return Success(strings.ToArray(), out normalized);
    }

    private static bool TryNormalizeInteger(object value, out long result)
    {
        switch (value)
        {
            case byte byteValue:
                result = byteValue;
                return true;
            case sbyte sbyteValue:
                result = sbyteValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case decimal decimalValue when decimalValue >= long.MinValue
                && decimalValue <= long.MaxValue
                && decimal.Truncate(decimalValue) == decimalValue:
                result = (long)decimalValue;
                return true;
            case ulong ulongValue when ulongValue <= long.MaxValue:
                result = (long)ulongValue;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryNormalizeDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case float floatValue:
                return TryConvertFloatingPointDecimal(floatValue, out result);
            case double doubleValue:
                return TryConvertFloatingPointDecimal(doubleValue, out result);
            default:
                result = default;
                return false;
        }
    }

    private static bool TryConvertFloatingPointDecimal(float value, out decimal result)
    {
        if (!float.IsFinite(value))
        {
            result = default;
            return false;
        }

        try
        {
            result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static bool TryConvertFloatingPointDecimal(double value, out decimal result)
    {
        if (!double.IsFinite(value))
        {
            result = default;
            return false;
        }

        try
        {
            result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static bool Success<T>(T value, out object normalized)
    {
        normalized = value!;
        return true;
    }

    private static bool Failure(out object normalized)
    {
        normalized = string.Empty;
        return false;
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : string.Create(value.Length, value, static (chars, source) =>
            {
                chars[0] = char.ToLowerInvariant(source[0]);
                source.AsSpan(1).CopyTo(chars[1..]);
            });

    private static IndexProjectionFailure Failure(IndexProjection projection, string code, string message) =>
        new(projection.FieldName, code, message, projection.Source.Describe());
}
