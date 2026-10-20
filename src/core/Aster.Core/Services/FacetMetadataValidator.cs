using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;

namespace Aster.Core.Services;

/// <summary>
/// Registration-time self-validation of declared facet metadata. Invoked by both the
/// in-memory definition store and the SQLite JSON provider at the top of
/// <c>RegisterDefinitionAsync</c>, before any state change, so both providers converge
/// on identical outcomes and diagnostic codes by construction.
/// </summary>
public static class FacetMetadataValidator
{
    /// <summary>
    /// Validates every metadata-bearing facet across all aspect attachments of a definition.
    /// </summary>
    /// <param name="definition">The definition being registered.</param>
    /// <exception cref="FacetMetadataValidationException">
    /// Thrown when any facet metadata is invalid; carries every violation across all aspects
    /// and facets, ordered by aspect key, then facet definition id, then code (ordinal).
    /// </exception>
    public static void Validate(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var diagnostics = new List<FacetMetadataDiagnostic>();

        foreach (var (aspectKey, aspectDefinition) in definition.AspectDefinitions)
        {
            foreach (var facet in aspectDefinition.FacetDefinitions)
            {
                if (ClassifyFacetType(facet.Type) == FacetValueType.Unknown)
                {
                    // Metadata applicability depends on the type, so unknown types skip metadata checks.
                    diagnostics.Add(new FacetMetadataDiagnostic(
                        FacetMetadataDiagnosticCodes.TypeUnknown,
                        $"Facet type '{facet.Type}' is not supported; expected string, int, decimal, bool, datetime, or guid.",
                        aspectKey,
                        facet.FacetDefinitionId));
                    continue;
                }

                if (facet.Metadata is null)
                    continue;

                ValidateFacet(aspectKey, facet, facet.Metadata, diagnostics);
            }
        }

        if (diagnostics.Count == 0)
            return;

        diagnostics.Sort(static (left, right) =>
        {
            var byAspect = string.CompareOrdinal(left.AspectKey, right.AspectKey);
            if (byAspect != 0)
                return byAspect;

            var byFacet = string.CompareOrdinal(left.FacetDefinitionId, right.FacetDefinitionId);
            return byFacet != 0 ? byFacet : string.CompareOrdinal(left.Code, right.Code);
        });

        throw new FacetMetadataValidationException(diagnostics);
    }

    private static void ValidateFacet(
        string aspectKey,
        FacetDefinition facet,
        FacetMetadata metadata,
        List<FacetMetadataDiagnostic> diagnostics)
    {
        var facetType = ClassifyFacetType(facet.Type);

        void Add(string code, string message) =>
            diagnostics.Add(new FacetMetadataDiagnostic(code, message, aspectKey, facet.FacetDefinitionId));

        // Constraint applicability per the declared-type matrix.
        var lengthApplicable = facetType == FacetValueType.String;
        var patternApplicable = facetType == FacetValueType.String;
        var rangeApplicable = facetType is FacetValueType.Int or FacetValueType.Decimal;
        var allowedValuesApplicable = facetType != FacetValueType.Bool;

        if (!lengthApplicable && (metadata.MinLength is not null || metadata.MaxLength is not null))
            Add(FacetMetadataDiagnosticCodes.ConstraintInapplicable,
                $"Length bounds apply to string facets only; facet type is '{facet.Type}'.");

        if (!patternApplicable && metadata.Pattern is not null)
            Add(FacetMetadataDiagnosticCodes.ConstraintInapplicable,
                $"Pattern applies to string facets only; facet type is '{facet.Type}'.");

        if (!rangeApplicable && (metadata.Minimum is not null || metadata.Maximum is not null))
            Add(FacetMetadataDiagnosticCodes.ConstraintInapplicable,
                $"Numeric range bounds apply to int and decimal facets only; facet type is '{facet.Type}'.");

        if (!allowedValuesApplicable && metadata.AllowedValues is not null)
            Add(FacetMetadataDiagnosticCodes.ConstraintInapplicable,
                $"Allowed values do not apply to bool facets; facet type is '{facet.Type}'.");

        // Bound coherence for applicable constraints.
        if (lengthApplicable)
        {
            if (metadata.MinLength is < 0 || metadata.MaxLength is < 0)
                Add(FacetMetadataDiagnosticCodes.RangeInvalid, "Length bounds must be non-negative.");
            else if (metadata is { MinLength: { } min, MaxLength: { } max } && min > max)
                Add(FacetMetadataDiagnosticCodes.RangeInvalid,
                    $"MinLength ({min}) must be less than or equal to MaxLength ({max}).");
        }

        if (rangeApplicable)
        {
            if (metadata is { Minimum: { } minimum, Maximum: { } maximum } && minimum > maximum)
                Add(FacetMetadataDiagnosticCodes.RangeInvalid,
                    $"Minimum ({minimum}) must be less than or equal to Maximum ({maximum}).");

            if (facetType == FacetValueType.Int
                && ((metadata.Minimum is { } wholeMin && decimal.Truncate(wholeMin) != wholeMin)
                    || (metadata.Maximum is { } wholeMax && decimal.Truncate(wholeMax) != wholeMax)))
            {
                Add(FacetMetadataDiagnosticCodes.RangeInvalid,
                    "Numeric bounds on an int facet must be whole numbers.");
            }
        }

        // Pattern well-formedness.
        var patternWellFormed = false;
        if (patternApplicable && metadata.Pattern is { } pattern)
        {
            try
            {
                _ = new Regex(pattern);
                patternWellFormed = true;
            }
            catch (ArgumentException)
            {
                Add(FacetMetadataDiagnosticCodes.PatternInvalid,
                    $"Pattern '{pattern}' is not a well-formed .NET regular expression.");
            }
        }

        // Allowed values: non-empty, type-matched, distinct after normalization.
        List<object>? normalizedAllowedValues = null;
        if (allowedValuesApplicable && metadata.AllowedValues is { } allowedValues)
        {
            if (allowedValues.Count == 0)
            {
                Add(FacetMetadataDiagnosticCodes.AllowedValuesEmpty,
                    "AllowedValues must contain at least one entry when declared.");
            }
            else
            {
                normalizedAllowedValues = [];
                var anyMismatch = false;
                var anyDuplicate = false;

                foreach (var entry in allowedValues)
                {
                    if (!TryNormalizeValue(entry, facetType, out var normalized))
                    {
                        anyMismatch = true;
                        continue;
                    }

                    if (normalizedAllowedValues.Contains(normalized))
                        anyDuplicate = true;
                    else
                        normalizedAllowedValues.Add(normalized);
                }

                if (anyMismatch)
                    Add(FacetMetadataDiagnosticCodes.AllowedValueTypeMismatch,
                        $"One or more AllowedValues entries do not normalize to declared type '{facet.Type}'.");

                if (anyDuplicate)
                    Add(FacetMetadataDiagnosticCodes.AllowedValuesDuplicate,
                        "AllowedValues contains duplicate entries after normalization.");
            }
        }

        // Default value: declared type plus every declared constraint.
        if (metadata.DefaultValue is { } defaultValue)
        {
            if (!TryNormalizeValue(defaultValue, facetType, out var normalizedDefault))
            {
                Add(FacetMetadataDiagnosticCodes.DefaultValueInvalid,
                    $"DefaultValue does not normalize to declared type '{facet.Type}'.");
            }
            else if (!DefaultSatisfiesConstraints(
                normalizedDefault, facetType, metadata, normalizedAllowedValues, patternWellFormed))
            {
                Add(FacetMetadataDiagnosticCodes.DefaultValueInvalid,
                    "DefaultValue violates one or more declared constraints.");
            }
        }
    }

    private static bool DefaultSatisfiesConstraints(
        object normalizedDefault,
        FacetValueType facetType,
        FacetMetadata metadata,
        List<object>? normalizedAllowedValues,
        bool patternWellFormed)
    {
        if (normalizedAllowedValues is { Count: > 0 } && !normalizedAllowedValues.Contains(normalizedDefault))
            return false;

        if (facetType == FacetValueType.String && normalizedDefault is string text)
        {
            if (metadata.MinLength is { } minLength && text.Length < minLength)
                return false;

            if (metadata.MaxLength is { } maxLength && text.Length > maxLength)
                return false;

            if (patternWellFormed && metadata.Pattern is { } pattern
                && !EvaluatePattern(text, pattern))
            {
                return false;
            }
        }

        if (facetType is FacetValueType.Int or FacetValueType.Decimal)
        {
            var numeric = normalizedDefault switch
            {
                long integer => (decimal)integer,
                decimal decimalValue => decimalValue,
                _ => (decimal?)null,
            };

            if (numeric is { } value)
            {
                if (metadata.Minimum is { } minimum && value < minimum)
                    return false;

                if (metadata.Maximum is { } maximum && value > maximum)
                    return false;
            }
        }

        return true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Shared type-normalization helpers (used by FacetValueValidator too)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Facet value types for which typed comparison semantics are defined.
    /// </summary>
    internal enum FacetValueType
    {
        /// <summary>The declared type is not one of the known descriptors.</summary>
        Unknown,

        /// <summary>String values; ordinal comparison and UTF-16 length semantics.</summary>
        String,

        /// <summary>Integer values; normalized to <see cref="long"/>.</summary>
        Int,

        /// <summary>Decimal values; normalized to <see cref="decimal"/>.</summary>
        Decimal,

        /// <summary>Boolean values.</summary>
        Bool,

        /// <summary>Date/time values; normalized to a UTC <see cref="DateTime"/> instant.</summary>
        DateTime,

        /// <summary>GUID values; normalized to <see cref="System.Guid"/>.</summary>
        Guid,
    }

    /// <summary>
    /// Classifies a declared facet type descriptor into typed comparison semantics.
    /// Accepts both documented descriptors ("int", "bool") and CLR-derived spellings
    /// produced by the obsolete <c>WithTypedFacet&lt;T&gt;</c> ("int32", "boolean").
    /// </summary>
    internal static FacetValueType ClassifyFacetType(string? type) => type?.ToLowerInvariant() switch
    {
        "string" => FacetValueType.String,
        "int" or "int32" or "int64" or "long" => FacetValueType.Int,
        "decimal" => FacetValueType.Decimal,
        "bool" or "boolean" => FacetValueType.Bool,
        "datetime" or "datetimeoffset" => FacetValueType.DateTime,
        "guid" => FacetValueType.Guid,
        _ => FacetValueType.Unknown,
    };

    /// <summary>
    /// Normalizes a value to the facet's declared type for comparison:
    /// string (ordinal), int (<see cref="long"/>), <see cref="decimal"/>, <see cref="bool"/>,
    /// datetime (UTC <see cref="DateTime"/>), and guid (<see cref="Guid"/>, from a value or any
    /// parseable string), tolerating <see cref="JsonElement"/> inputs
    /// after JSON round-trips.
    /// </summary>
    internal static bool TryNormalizeValue(object? value, FacetValueType facetType, out object normalized)
    {
        normalized = null!;

        if (value is JsonElement element)
            return TryNormalizeJsonElement(element, facetType, out normalized);

        switch (facetType)
        {
            case FacetValueType.String when value is string text:
                normalized = text;
                return true;

            case FacetValueType.Int when TryNormalizeInteger(value, out var integer):
                normalized = integer;
                return true;

            case FacetValueType.Decimal when TryNormalizeDecimal(value, out var decimalValue):
                normalized = decimalValue;
                return true;

            case FacetValueType.Bool when value is bool boolean:
                normalized = boolean;
                return true;

            case FacetValueType.DateTime when TryNormalizeDateTime(value, out var instant):
                normalized = instant;
                return true;

            case FacetValueType.Guid when value is Guid guid:
                normalized = guid;
                return true;

            case FacetValueType.Guid when value is string guidText && Guid.TryParse(guidText, out var parsedGuid):
                normalized = parsedGuid;
                return true;

            default:
                return false;
        }
    }

    private static bool TryNormalizeJsonElement(JsonElement element, FacetValueType facetType, out object normalized)
    {
        normalized = null!;

        switch (facetType)
        {
            case FacetValueType.String when element.ValueKind == JsonValueKind.String:
                normalized = element.GetString()!;
                return true;

            case FacetValueType.Int when element.ValueKind == JsonValueKind.Number
                && element.TryGetInt64(out var integer):
                normalized = integer;
                return true;

            case FacetValueType.Decimal when element.ValueKind == JsonValueKind.Number
                && element.TryGetDecimal(out var decimalValue):
                normalized = decimalValue;
                return true;

            case FacetValueType.Bool when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                normalized = element.GetBoolean();
                return true;

            case FacetValueType.DateTime when element.ValueKind == JsonValueKind.String
                && TryNormalizeDateTime(element.GetString(), out var instant):
                normalized = instant;
                return true;

            case FacetValueType.Guid when element.ValueKind == JsonValueKind.String
                && Guid.TryParse(element.GetString(), out var guid):
                normalized = guid;
                return true;

            default:
                return false;
        }
    }

    private static bool TryNormalizeInteger(object? value, out long result)
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
            case ulong ulongValue when ulongValue <= long.MaxValue:
                result = (long)ulongValue;
                return true;
            case decimal decimalValue when decimalValue >= long.MinValue
                && decimalValue <= long.MaxValue
                && decimal.Truncate(decimalValue) == decimalValue:
                result = (long)decimalValue;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryNormalizeDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case float floatValue when float.IsFinite(floatValue):
                try
                {
                    result = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (OverflowException)
                {
                    result = default;
                    return false;
                }

            case double doubleValue when double.IsFinite(doubleValue):
                try
                {
                    result = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (OverflowException)
                {
                    result = default;
                    return false;
                }

            default:
                result = default;
                return false;
        }
    }

    private static bool TryNormalizeDateTime(object? value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dateTime:
                result = dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime();
                return true;

            case DateTimeOffset dateTimeOffset:
                result = dateTimeOffset.UtcDateTime;
                return true;

            case string text when DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed):
                result = parsed.UtcDateTime;
                return true;

            default:
                result = default;
                return false;
        }
    }

    /// <summary>
    /// Evaluates a pattern with standard .NET semantics under a bounded match timeout.
    /// A match that exceeds the bound fails deterministically.
    /// </summary>
    internal static bool EvaluatePattern(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
