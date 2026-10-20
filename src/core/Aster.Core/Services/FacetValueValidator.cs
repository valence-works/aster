using Aster.Core.Models.Definitions;
using static Aster.Core.Services.FacetMetadataValidator;

namespace Aster.Core.Services;

/// <summary>
/// Save-time validation of facet values against their declared type, <c>IsRequired</c>, and
/// declared metadata constraints. Invoked by <c>DefaultResourceManager</c> and
/// <c>InMemoryResourceManager</c> create/update paths before any before-save lifecycle hook and
/// before any write, so a violation means nothing was persisted. Portability import and schema
/// upgrades do not call it, so historical versions stay importable and upgradable.
/// </summary>
public static class FacetValueValidator
{
    /// <summary>
    /// Validates every facet of every aspect attachment that has a payload: missing required
    /// values, values that do not match the declared type, then declared metadata constraints. Array
    /// facets are checked element by element; element diagnostics carry
    /// <see cref="FacetValueDiagnostic.ElementIndex"/>.
    /// </summary>
    /// <param name="definition">The resolved resource definition.</param>
    /// <param name="aspects">The resource's aspect payloads keyed by attachment key.</param>
    /// <returns>
    /// All violations, ordered by aspect key, then facet definition id, then code (ordinal), then
    /// element index; empty when every present value conforms.
    /// </returns>
    public static IReadOnlyList<FacetValueDiagnostic> Validate(
        ResourceDefinition definition,
        IReadOnlyDictionary<string, object> aspects)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(aspects);

        var diagnostics = new List<FacetValueDiagnostic>();

        foreach (var (aspectKey, aspectDefinition) in definition.AspectDefinitions)
        {
            if (!aspects.TryGetValue(aspectKey, out var payload) || payload is null)
                continue;

            foreach (var facet in aspectDefinition.FacetDefinitions)
                ValidateFacetValue(aspectKey, facet, payload, diagnostics);
        }

        if (diagnostics.Count > 1)
        {
            diagnostics.Sort(static (left, right) =>
            {
                var byAspect = string.CompareOrdinal(left.AspectKey, right.AspectKey);
                if (byAspect != 0)
                    return byAspect;

                var byFacet = string.CompareOrdinal(left.FacetDefinitionId, right.FacetDefinitionId);
                if (byFacet != 0)
                    return byFacet;

                var byCode = string.CompareOrdinal(left.Code, right.Code);
                return byCode != 0 ? byCode : Nullable.Compare(left.ElementIndex, right.ElementIndex);
            });
        }

        return diagnostics;
    }

    private static void ValidateFacetValue(
        string aspectKey,
        FacetDefinition facet,
        object payload,
        List<FacetValueDiagnostic> diagnostics)
    {
        // Unknown types are rejected at registration; definitions stored before that rule are skipped.
        var descriptor = ParseFacetType(facet.Type);
        if (!descriptor.IsKnown)
            return;

        void Add(string code, string message, int? elementIndex = null) =>
            diagnostics.Add(new FacetValueDiagnostic(code, message, aspectKey, facet.FacetDefinitionId)
            {
                ElementIndex = elementIndex,
            });

        if (!AspectFacetValueResolver.TryResolveValue(payload, facet.FacetDefinitionId, out var rawValue))
        {
            if (facet.IsRequired)
                Add(FacetValueDiagnosticCodes.Required, "A value is required.");

            return;
        }

        if (descriptor.IsArray)
        {
            ValidateArrayValue(facet, descriptor.Element, rawValue, Add);
            return;
        }

        if (!TryNormalizeValue(rawValue, descriptor.Element, out var value))
        {
            Add(FacetValueDiagnosticCodes.TypeMismatch, $"Value does not match the declared type '{facet.Type}'.");
            return;
        }

        if (facet.Metadata is { } metadata)
            ValidateConstraints(value, descriptor.Element, metadata, (code, message) => Add(code, message));
    }

    private static void ValidateArrayValue(
        FacetDefinition facet,
        FacetValueType elementType,
        object? rawValue,
        Action<string, string, int?> add)
    {
        if (!TryGetArrayElements(rawValue, out var elements))
        {
            add(FacetValueDiagnosticCodes.TypeMismatch, $"Value does not match the declared type '{facet.Type}'; expected an array.", null);
            return;
        }

        // Required means at least one element, matching the portable Exists semantics for arrays.
        if (elements.Count == 0 && facet.IsRequired)
        {
            add(FacetValueDiagnosticCodes.Required, "At least one element is required.", null);
            return;
        }

        var values = new List<object>(elements.Count);
        for (var index = 0; index < elements.Count; index++)
        {
            if (TryNormalizeValue(elements[index], elementType, out var normalized))
                values.Add(normalized);
            else
                add(FacetValueDiagnosticCodes.TypeMismatch,
                    $"Element [{index}] does not match the declared type '{facet.Type}'.", index);
        }

        // Constraints apply only to well-typed values, as for scalar facets.
        if (values.Count != elements.Count || facet.Metadata is not { } metadata)
            return;

        if ((metadata.MinItems is { } minItems && values.Count < minItems)
            || (metadata.MaxItems is { } maxItems && values.Count > maxItems))
        {
            add(FacetValueDiagnosticCodes.ItemsOutOfRange,
                $"Element count ({values.Count}) is outside the declared item count bounds.", null);
        }

        if (metadata.UniqueItems is true)
        {
            var seen = new HashSet<object>();
            for (var index = 0; index < values.Count; index++)
            {
                if (!seen.Add(values[index]))
                {
                    add(FacetValueDiagnosticCodes.ItemsNotUnique,
                        $"Element [{index}] duplicates an earlier element.", index);
                    break;
                }
            }
        }

        for (var index = 0; index < values.Count; index++)
        {
            var elementIndex = index;
            ValidateConstraints(values[index], elementType, metadata,
                (code, message) => add(code, $"Element [{elementIndex}]: {message}", elementIndex));
        }
    }

    /// <summary>
    /// Checks a normalized scalar value (or one normalized array element) against the declared
    /// value constraints: AllowedValues, string length and pattern, and numeric range.
    /// </summary>
    private static void ValidateConstraints(
        object value,
        FacetValueType facetType,
        FacetMetadata metadata,
        Action<string, string> add)
    {
        // AllowedValues.
        if (facetType != FacetValueType.Bool && metadata.AllowedValues is { Count: > 0 } allowedValues)
        {
            var allowed = false;
            foreach (var entry in allowedValues)
            {
                if (TryNormalizeValue(entry, facetType, out var normalizedEntry) && normalizedEntry.Equals(value))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
                add(FacetValueDiagnosticCodes.NotAllowed, "Value is not in the declared AllowedValues list.");
        }

        // String length bounds and pattern.
        if (facetType == FacetValueType.String && value is string text)
        {
            if ((metadata.MinLength is { } minLength && text.Length < minLength)
                || (metadata.MaxLength is { } maxLength && text.Length > maxLength))
            {
                add(FacetValueDiagnosticCodes.LengthOutOfRange,
                    $"Value length ({text.Length}) is outside the declared length bounds.");
            }

            if (metadata.Pattern is { } pattern)
            {
                bool matches;
                try
                {
                    matches = EvaluatePattern(text, pattern);
                }
                catch (ArgumentException)
                {
                    // A malformed pattern cannot be evaluated; registration is the
                    // well-formedness gate, so skip the check for this value.
                    matches = true;
                }

                if (!matches)
                    add(FacetValueDiagnosticCodes.PatternMismatch, "Value does not match the declared pattern.");
            }
        }

        // Numeric range bounds (integer comparison for int facets, decimal for decimal facets;
        // decimal represents every long exactly, so one comparison path serves both).
        if (facetType is FacetValueType.Int or FacetValueType.Decimal)
        {
            var numeric = value switch
            {
                long integer => (decimal)integer,
                decimal decimalValue => decimalValue,
                _ => (decimal?)null,
            };

            if (numeric is { } numericValue
                && ((metadata.Minimum is { } minimum && numericValue < minimum)
                    || (metadata.Maximum is { } maximum && numericValue > maximum)))
            {
                add(FacetValueDiagnosticCodes.OutOfRange,
                    $"Value ({numericValue}) is outside the declared numeric range.");
            }
        }
    }
}
